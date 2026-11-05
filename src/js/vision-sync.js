
/* =====================================================================
   ARCHIVISION — synchronisation des maquettes et photos entre appareils (compte Studio Prism).
   Firestore : avModels/{id} et avPhotos/{id} (métadonnées, ownerUid) ; Storage : archivision/{uid}/… (fichiers).
   Les fichiers sont envoyés AVANT la fiche : une fiche visible = fichiers disponibles. Écoute temps réel (onSnapshot).
   Suppression : fiche d'abord, fichiers ensuite ; une liste « à supprimer » (localStorage) rejoue la suppression hors ligne.
   Le local reste la référence hors ligne : sans règles publiées ou sans réseau, l'app fonctionne comme avant (état affiché dans l'accueil).
   ===================================================================== */
const AVSync = (() => {
  const S = {state:'off', msg:''};
  let uid = null, T = null, unsubs = [], rM = null, rP = null, running = false, again = false, timer = 0, gen = 0;
  const MPATH = (u, id, f) => `archivision/${u}/models/${id}/${f}`, PPATH = (u, id) => `archivision/${u}/photos/${id}`;
  const tombs = () => (uid && store.get('av.sync.del.' + uid)) || [];
  const setTombs = a => store.set('av.sync.del.' + uid, a);
  const clean = o => JSON.parse(JSON.stringify(o));
  const set = (state, msg = '') => { S.state = state; S.msg = msg; try { window.AVUI?.syncChanged?.(); } catch(e){} };
  const isPerm = e => /permission|unauthorized|unauthenticated/i.test(e?.code || '') ;

  function fbTransport(){
    const db = Auth.db; let sp = null;
    const storage = () => sp || (sp = (async () => { if (!firebase.storage) await loadScript(`https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}/firebase-storage-compat.js`); return firebase.storage(); })());
    return {
      listen:(col, u, cb, err) => db.collection(col).where('ownerUid', '==', u).onSnapshot({includeMetadataChanges:true}, sn => { if (sn.metadata.fromCache && !sn.metadata.hasPendingWrites) return; cb(sn.docs.map(d => ({id:d.id, ...d.data()}))); }, err),
      setDoc:(col, id, d) => db.collection(col).doc(id).set(d),
      delDoc:(col, id) => db.collection(col).doc(id).delete(),
      async putBlob(path, blob){ await (await storage()).ref(path).put(blob, {contentType:blob.type || 'application/octet-stream'}); },
      async getBlob(path){ const r = (await storage()).ref(path); if (typeof r.getBlob === 'function') return r.getBlob(); const x = await fetch(await r.getDownloadURL()); if (!x.ok) throw {code:'storage/unknown'}; return x.blob(); },
      async delBlob(path){ try { await (await storage()).ref(path).delete(); } catch(e){ if (e?.code !== 'storage/object-not-found') throw e; } },
    };
  }

  const mDoc = m => clean({ownerUid:uid, name:m.name, kind:m.kind, size:m.size || 0, unitScale:m.unitScale || 0, thumb:m.thumb || '', stats:m.stats || null, createdAt:m.createdAt, updatedAt:m.updatedAt, glbSize:m.glb?.size || m.glbSize || 0, hasSource:!!(m.source || m.sourceSize)});
  const pDoc = p => clean({ownerUid:uid, modelId:p.modelId, modelName:p.modelName || '', size:p.size || 0, w:p.w || 0, h:p.h || 0, thumb:p.thumb || '', note:p.note || '', mode:p.mode || '', createdAt:p.createdAt, updatedAt:p.updatedAt});
  const strip = r => { const {id, ownerUid, ...d} = r; return d; };

  async function pushModel(l, isNew){
    if (isNew){
      const f = await AVStore.getModel(l.id); if (!f?.glb) return;
      await T.putBlob(MPATH(uid, l.id, 'model.glb'), f.glb); if (f.source) await T.putBlob(MPATH(uid, l.id, 'source.bin'), f.source);
      l = {...f, updatedAt:l.updatedAt};
    }
    await T.setDoc('avModels', l.id, mDoc(l)); await AVStore.markSynced('model', l.id, l.updatedAt);
  }
  async function pushPhoto(l, isNew){
    if (isNew){ const f = await AVStore.getPhoto(l.id); if (!f?.blob) return; await T.putBlob(PPATH(uid, l.id), f.blob); l = {...f, updatedAt:l.updatedAt}; }
    await T.setDoc('avPhotos', l.id, pDoc(l)); await AVStore.markSynced('photo', l.id, l.updatedAt);
  }
  async function pullModel(r, l){
    let rec;
    if (l){ rec = await AVStore.getModel(l.id); Object.assign(rec, strip(r), {id:l.id, uid, syncedAt:r.updatedAt}); }
    else { const glb = await T.getBlob(MPATH(uid, r.id, 'model.glb')); const source = r.hasSource ? await T.getBlob(MPATH(uid, r.id, 'source.bin')) : undefined;
      rec = {...strip(r), id:r.id, uid, glb, source, openedAt:r.createdAt, syncedAt:r.updatedAt}; delete rec.hasSource; }
    await AVStore.putModel(rec, {sync:true});
  }
  async function pullPhoto(r, l){
    let rec;
    if (l){ rec = await AVStore.getPhoto(l.id); Object.assign(rec, strip(r), {id:l.id, uid, syncedAt:r.updatedAt}); }
    else { const blob = await T.getBlob(PPATH(uid, r.id)); rec = {...strip(r), id:r.id, uid, blob, syncedAt:r.updatedAt}; }
    await AVStore.putPhoto(rec, {sync:true});
  }

  async function reconcile(){
    // 1. suppressions en attente (faites sur cet appareil)
    for (const t of tombs()){
      try {
        await T.delDoc(t.c, t.id);
        for (const p of t.paths) await T.delBlob(p);
        setTombs(tombs().filter(x => !(x.c === t.c && x.id === t.id)));
      } catch(e){ if (isPerm(e)) throw e; }
    }
    const dead = new Set(tombs().map(t => t.c + t.id));
    // 2. fiches : modèles puis photos
    const sets = [['avModels', rM, await AVStore.listModels(uid), pushModel, pullModel, id => AVStore.deleteModel(id, {silent:true})],
                  ['avPhotos', rP, await AVStore.listPhotos(uid), pushPhoto, pullPhoto, id => AVStore.deletePhoto(id, {silent:true})]];
    let changed = false;
    for (const [col, remote, local, push, pull, delLocal] of sets){
      const rmap = new Map(remote.map(r => [r.id, r])), lmap = new Map(local.map(l => [l.id, l]));
      for (const r of remote){
        if (dead.has(col + r.id)) continue;
        const l = lmap.get(r.id);
        if (!l){ await pull(r, null); changed = true; }
        else if ((r.updatedAt || 0) > (l.updatedAt || 0)){ await pull(r, l); changed = true; }
        else if ((l.updatedAt || 0) > (r.updatedAt || 0)) await push(l, false);
      }
      for (const l of local){
        if (rmap.has(l.id)) continue;
        if (l.syncedAt != null){ await delLocal(l.id); changed = true; }   // supprimée depuis un autre appareil
        else await push({...l, updatedAt:l.updatedAt || l.createdAt || Date.now()}, true);   // nouvelle ici
      }
    }
    return changed;
  }

  async function run(){
    if (!T || !uid || rM == null || rP == null) return;
    if (running){ again = true; return; }
    running = true; const g = gen; set('busy', 'Synchronisation…');
    try {
      do { again = false; if (await reconcile() && g === gen) window.AVUI?.refreshHome?.(); } while (again && g === gen);
      if (g === gen) set('ok', 'À jour');
    } catch(e){
      if (g !== gen) return;
      console.warn('AVSync', e);
      if (isPerm(e)) set('error', 'Synchronisation non activée sur le serveur (règles Firestore / Storage à publier).');
      else { set('error', navigator.onLine ? 'Synchronisation interrompue, nouvel essai bientôt.' : 'Hors ligne : synchronisation en attente.'); clearTimeout(timer); timer = setTimeout(run, 30000); }
    } finally { running = false; }
  }

  return {
    S,
    /** À l'ouverture de l'accueil : démarre l'écoute pour le compte connecté (une seule fois par compte). */
    start(transport){
      if (!me || (!transport && Auth.mode !== 'firebase')) return;
      if (uid === me.uid && !transport) return;
      this.stop(); uid = me.uid; const my = uid, g = ++gen; T = transport || fbTransport(); set('busy', 'Connexion…');
      const err = e => { if (g !== gen) return; console.warn('AVSync listen', e); set('error', isPerm(e) ? 'Synchronisation non activée sur le serveur (règles Firestore / Storage à publier).' : 'Synchronisation indisponible pour le moment.'); };
      try {
        unsubs = [T.listen('avModels', my, l => { rM = l; run(); }, err), T.listen('avPhotos', my, l => { rP = l; run(); }, err)];
      } catch(e){ err(e); }
    },
    stop(){ gen++; unsubs.forEach(u => { try { u(); } catch(e){} }); unsubs = []; uid = null; T = null; rM = rP = null; clearTimeout(timer); set('off'); },
    now(){ clearTimeout(timer); return run(); },
    /** Une maquette ou une photo a changé ici : envoi dès que possible. */
    kick(){ if (T) { clearTimeout(timer); timer = setTimeout(run, 400); } },
    /** Suppression faite ici : à répercuter (fiche puis fichiers). */
    tomb(kind, id){ if (!uid) return; const u = uid; const c = kind === 'model' ? 'avModels' : 'avPhotos';
      const paths = kind === 'model' ? [MPATH(u, id, 'model.glb'), MPATH(u, id, 'source.bin')] : [PPATH(u, id)];
      setTombs([...tombs().filter(x => !(x.c === c && x.id === id)), {c, id, paths}]); this.kick(); },
  };
})();
window.AVSync = AVSync;
window.addEventListener('online', () => AVSync.now());
