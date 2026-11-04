/* ============ « Voir en AR sur le PC » : session par code, déplacement d'éléments en direct ============
   Protocole complet : docs/AR_LIVE.md (à reprendre dans l'app Unity mobile).
   - Hôte = le téléphone en AR : crée la session (code court), publie la liste des éléments (id, nom, boîte), applique les déplacements reçus.
   - PC = saisit le code, voit les éléments sous forme de boîtes, ne peut que DÉPLACER (position), envoie les positions.
   Transport : Firestore (arLive/{code} + sous-collection moves) ; en mode démo, BroadcastChannel + localStorage du navigateur (tests). */
const LIVE_TTL = 2 * 3600e3, LIVE_ALPHA = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789', LIVE_MAX = 300;
const LIVE_CLIENT = Math.random().toString(36).slice(2, 10);
const liveNorm = c => String(c || '').toUpperCase().replace(/[^A-Z0-9]/g, '').replace(/O/g, '0').replace(/I|L/g, '1').slice(0, 6);
const liveNewCode = () => Array.from(crypto.getRandomValues(new Uint8Array(6)), b => LIVE_ALPHA[b % 32]).join('');
const liveUid = () => window.AVUser?.()?.uid || 'local';

const LiveT = (() => {
  const fs = () => window.AVDb?.();
  if (fs()){
    const doc = c => fs().collection('arLive').doc(c), ts = firebase.firestore.Timestamp;
    return {
      mode:'firestore',
      async create(c, meta){ await doc(c).set({...meta, createdAt:firebase.firestore.FieldValue.serverTimestamp(), expiresAt:ts.fromMillis(meta.expires)}); },
      async get(c){ const s = await doc(c).get(); if (!s.exists) return null; const d = s.data(); const ex = d.expiresAt?.toMillis?.() || 0; return ex > Date.now() ? {...d, expires:ex} : null; },
      onSession(c, cb){ return doc(c).onSnapshot(s => s.exists && cb(s.data()), () => {}); },
      onMoves(c, cb){ return doc(c).collection('moves').onSnapshot(sn => sn.docChanges().forEach(ch => ch.type !== 'removed' && cb(ch.doc.data())), () => {}); },
      async move(c, m){ await doc(c).collection('moves').doc(String(m.i)).set({i:m.i, p:m.p, seq:m.seq, c:LIVE_CLIENT, t:firebase.firestore.FieldValue.serverTimestamp()}); },
      async close(c){ await doc(c).update({state:'closed'}); },
    };
  }
  /* mode démo : même interface, entre onglets du même navigateur */
  const ch = 'BroadcastChannel' in window ? new BroadcastChannel('avlive') : null, K = c => 'avlive:' + c, subs = new Set();
  ch && (ch.onmessage = e => subs.forEach(f => f(e.data)));
  const rd = c => { try { return JSON.parse(localStorage.getItem(K(c)) || 'null'); } catch(e){ return null; } };
  return {
    mode:'local',
    async create(c, meta){ localStorage.setItem(K(c), JSON.stringify({meta, moves:{}})); },
    async get(c){ const r = rd(c); return r && r.meta.expires > Date.now() ? r.meta : null; },
    onSession(c, cb){ const f = m => m.code === c && m.type === 'session' && cb(m.meta); subs.add(f); return () => subs.delete(f); },
    onMoves(c, cb){ Object.values(rd(c)?.moves || {}).forEach(cb); const f = m => m.code === c && m.type === 'move' && cb(m.move); subs.add(f); return () => subs.delete(f); },
    async move(c, m){ const r = rd(c); if (!r) return; const mv = {i:m.i, p:m.p, seq:m.seq, c:LIVE_CLIENT}; r.moves[m.i] = mv; localStorage.setItem(K(c), JSON.stringify(r)); ch?.postMessage({code:c, type:'move', move:mv}); },
    async close(c){ const r = rd(c); if (!r) return; r.meta.state = 'closed'; localStorage.setItem(K(c), JSON.stringify(r)); ch?.postMessage({code:c, type:'session', meta:r.meta}); },
  };
})();
const liveOk = p => Array.isArray(p) && p.length === 3 && p.every(v => Number.isFinite(v) && Math.abs(v) < 1e4);
const r3 = v => Math.round(v * 1000) / 1000;

/* ---------- Hôte (téléphone en AR) ---------- */
const HOST = {code:null, map:new Map(), unsub:[]};
function hostCollect(){
  const out = [], SP = /^Ifc(Project|Site|Building|BuildingStorey)$/;
  const walk = (o, d) => { for (const c of kids(o)){
    if (out.length > 4000) return;
    const cls = c.userData?.ifc?.classe || '';
    if (hasInfo(c) && !SP.test(cls) && (isLeafEl(c) || c.isMesh)) out.push(c);
    else if (c.isMesh && !hasInfo(o) && d <= 1) out.push(c);
    else if (c.children.length) walk(c, d + 1);
  } };
  walk(V.root, 0);
  if (!out.length) kids(V.root).forEach(c => out.push(c));
  return out;
}
async function hostStart(){
  if (HOST.code || !V.root) return;
  const items = [], tmp = new THREE.Box3(), v = new THREE.Vector3();
  pivot.updateMatrixWorld(true);
  for (const o of hostCollect()){
    tmp.setFromObject(o); if (tmp.isEmpty()) continue;
    const c = tmp.getCenter(new THREE.Vector3()), sz = tmp.getSize(new THREE.Vector3());
    const cl = pivot.worldToLocal(c.clone()), k = pivot.scale.x || 1;
    items.push({o, vol:sz.x * sz.y * sz.z, e:{n:String(objLabel(o)).slice(0, 60), k:String(objKind(o) || '').slice(0, 30), p:[r3(cl.x), r3(cl.y), r3(cl.z)], s:[r3(sz.x / k), r3(sz.y / k), r3(sz.z / k)]}});
  }
  items.sort((a, b) => b.vol - a.vol); items.length = Math.min(items.length, LIVE_MAX);
  HOST.map.clear();
  const objects = items.map((it, i) => { HOST.map.set(i, {o:it.o, pos0:it.o.position.clone(), c0:new THREE.Vector3(...it.e.p), seq:-1}); return {i, ...it.e}; });
  let code = null;
  for (let t = 0; t < 6 && !code; t++){
    const c = liveNewCode();
    try { await LiveT.create(c, {v:1, code:c, owner:liveUid(), expires:Date.now() + LIVE_TTL, state:'open', model:String(V.rec?.name || 'Maquette').slice(0, 80), objects}); code = c; }
    catch(e){ console.warn('arLive', e); if (t === 5) return toast('Code PC indisponible (connexion ou règles Firestore).'); }
  }
  HOST.code = code;
  const chip = $('#arCode'); chip.textContent = 'Code PC : ' + code.slice(0, 3) + ' ' + code.slice(3); chip.hidden = false;
  HOST.unsub.push(LiveT.onMoves(code, m => { if (m.c !== LIVE_CLIENT) hostApply(m); }));
}
function hostApply(m){
  const e = HOST.map.get(m.i); if (!e || !liveOk(m.p) || !(m.seq > e.seq)) return;
  e.seq = m.seq; pivot.updateMatrixWorld(true);
  const dW = pivot.localToWorld(new THREE.Vector3(...m.p)).sub(pivot.localToWorld(e.c0.clone()));
  const par = e.o.parent; par.updateWorldMatrix(true, false);
  e.o.position.copy(par.worldToLocal(par.localToWorld(e.pos0.clone()).add(dW))); e.o.updateMatrixWorld(true);
  let nw = false; e.o.traverse(x => { if (x.isMesh && !BATCH.skip.has(x)){ BATCH.skip.add(x); x.layers.set(0); nw = true; } });
  if (nw) batchSoon(); markDirty();
}
function hostStop(){
  if (!HOST.code) return;
  HOST.unsub.forEach(f => f()); HOST.unsub = [];
  LiveT.close(HOST.code).catch(() => {});
  /* remet les éléments à leur place d'origine à la fin de la session AR */
  HOST.map.forEach(e => e.o.position.copy(e.pos0)); BATCH.skip.clear(); batchSoon();
  HOST.code = null; HOST.map.clear(); $('#arCode').hidden = true;
}

/* ---------- PC : saisie du code puis interface de déplacement ---------- */
const PC = {code:null, sess:null, ren:null, scene:null, cam:null, orb:null, tc:null, boxes:[], sel:-1, seq:0, sendT:0, unsub:[], raf:0, base:null};
function pcAskCode(){
  const ui = window.AVUI; if (!ui) return;
  ui.dlg(`<h3>Voir en AR sur le PC</h3><p class="dlg-p">Lancez la maquette en réalité augmentée sur le téléphone : un <b>code à 6 caractères</b> s'affiche dans l'application. Saisissez-le ici pour déplacer des éléments de la maquette, qui bougeront en direct sur le téléphone.</p>
    <input id="pcAsk" class="pc-ask" maxlength="7" autocomplete="off" autocapitalize="characters" spellcheck="false" placeholder="ABC 123" aria-label="Code de session"><p class="hint" id="pcErr" role="alert"></p>
    <div class="dlg-acts"><button type="button" class="ap-btn alt" data-dlg="close">Annuler</button><button type="button" class="ap-btn" id="pcGo">Ouvrir</button></div>`, d => {
    const inp = d.querySelector('#pcAsk'), err = d.querySelector('#pcErr'); inp.focus();
    const go = async () => {
      const c = liveNorm(inp.value); if (c.length !== 6) return err.textContent = 'Le code comporte 6 caractères.';
      err.textContent = 'Recherche de la session…';
      try { const s = await LiveT.get(c); if (!s) return err.textContent = 'Code inconnu ou expiré. Relancez la réalité augmentée sur le téléphone.'; if (s.state === 'closed') return err.textContent = 'Cette session AR est terminée.'; ui.closeDlg(); pcOpen(c, s); }
      catch(e){ console.warn(e); err.textContent = 'Connexion impossible (réseau ou droits). Vérifiez que vous êtes connecté.'; }
    };
    d.querySelector('#pcGo').onclick = go; inp.addEventListener('keydown', e => e.key === 'Enter' && go());
  });
}
const pcColor = k => { let h = 0; for (const ch of String(k || 'x')) h = (h * 31 + ch.charCodeAt(0)) % 360; return new THREE.Color().setHSL(h / 360, 0.45, 0.62); };
function pcOpen(code, sess){
  pcClose(true); PC.code = code; PC.sess = sess; PC.seq = Date.now() % 1e9;
  const root = $('#arPc'); root.hidden = false; document.body.dataset.pc = '1';
  $('#pcCode').textContent = 'Code ' + code.slice(0, 3) + ' ' + code.slice(3) + ' · ' + (sess.model || 'Maquette');
  $('#pcSt').textContent = 'Connecté'; $('#pcSt').dataset.on = '1';
  const cv = $('#pcCanvas');
  PC.ren = new THREE.WebGLRenderer({canvas:cv, antialias:true}); PC.ren.setPixelRatio(Math.min(devicePixelRatio, 2));
  PC.scene = new THREE.Scene(); PC.scene.background = new THREE.Color(0xEAF0F6);
  PC.scene.add(new THREE.HemisphereLight(0xffffff, 0x8899aa, 1.1)); const dl = new THREE.DirectionalLight(0xffffff, 1.2); dl.position.set(5, 10, 6); PC.scene.add(dl);
  PC.cam = new THREE.PerspectiveCamera(45, 1, 0.05, 2000);
  PC.orb = new OrbitControls(PC.cam, cv); PC.orb.enableDamping = true;
  PC.tc = new TransformControls(PC.cam, cv); PC.tc.setMode('translate'); PC.tc.setSpace('world'); PC.tc.setSize(0.9);   // uniquement déplacer
  PC.tc.addEventListener('dragging-changed', e => { PC.orb.enabled = !e.value; if (!e.value) pcSend(true); });
  PC.tc.addEventListener('objectChange', () => { pcSync(); pcSend(false); });
  PC.scene.add(PC.tc);
  const bb = new THREE.Box3();
  PC.boxes = (sess.objects || []).map(o => {
    const sz = o.s.map(x => Math.max(x, 0.02)), m = new THREE.Mesh(new THREE.BoxGeometry(sz[0], sz[1], sz[2]), new THREE.MeshStandardMaterial({color:pcColor(o.k), roughness:0.7, transparent:true, opacity:0.9}));
    m.add(new THREE.LineSegments(new THREE.EdgesGeometry(m.geometry), new THREE.LineBasicMaterial({color:0x33415c})));
    m.position.set(...o.p); m.userData.i = o.i; m.userData.p0 = o.p.slice(); PC.scene.add(m); bb.expandByObject(m); return m;
  });
  if (bb.isEmpty()) bb.set(new THREE.Vector3(-2, 0, -2), new THREE.Vector3(2, 2, 2));
  const gr = new THREE.GridHelper(Math.max(10, Math.ceil(bb.getSize(new THREE.Vector3()).length() * 1.5)), 20, 0x8aa0b8, 0xc4d2e0); gr.position.y = bb.min.y; PC.scene.add(gr);
  const c = bb.getCenter(new THREE.Vector3()), r = Math.max(bb.getSize(new THREE.Vector3()).length() * 0.8, 2);
  PC.cam.position.set(c.x + r, c.y + r * 0.7, c.z + r); PC.orb.target.copy(c); PC.orb.update();
  pcList(); pcSelect(-1);
  PC.unsub.push(LiveT.onMoves(code, m => { if (m.c !== LIVE_CLIENT) pcRemote(m); }));
  PC.unsub.push(LiveT.onSession(code, s => { if (s.state === 'closed'){ $('#pcSt').textContent = 'Session AR terminée sur le téléphone'; $('#pcSt').dataset.on = '0'; pcSelect(-1); } }));
  const loop = () => { PC.raf = requestAnimationFrame(loop); pcResize(); PC.orb.update(); PC.ren.render(PC.scene, PC.cam); }; loop();
}
function pcResize(){ const cv = PC.ren.domElement, w = cv.parentElement.clientWidth, h = cv.parentElement.clientHeight; if (cv.width !== Math.round(w * PC.ren.getPixelRatio()) || cv.height !== Math.round(h * PC.ren.getPixelRatio())){ PC.ren.setSize(w, h, false); PC.cam.aspect = w / Math.max(h, 1); PC.cam.updateProjectionMatrix(); } }
function pcRemote(m){ const b = PC.boxes[m.i]; if (b && liveOk(m.p) && m.i !== PC.sel){ b.position.set(...m.p); } }
function pcList(){
  const q = ($('#pcQ').value || '').toLowerCase(), ul = $('#pcUl');
  ul.innerHTML = (PC.sess.objects || []).filter(o => !q || (o.n + ' ' + o.k).toLowerCase().includes(q)).map(o => `<li><button type="button" data-i="${o.i}" aria-pressed="${o.i === PC.sel}"><b>${esc(o.n)}</b><small>${esc(o.k)}</small></button></li>`).join('') || '<li class="hint">Aucun élément.</li>';
}
function pcSelect(i){
  PC.sel = i; const b = PC.boxes[i];
  PC.boxes.forEach(x => { x.material.emissive?.set(x === b ? 0x2FD3B4 : 0x000000); x.material.emissiveIntensity = x === b ? 0.55 : 0; });
  if (b){ PC.tc.attach(b); $('#pcXyz').hidden = false; $('#pcHint').hidden = true; $('#pcSel').textContent = PC.sess.objects[i].n; pcSync(); } else { PC.tc.detach(); $('#pcXyz').hidden = true; $('#pcHint').hidden = false; }
  pcList();
}
function pcSync(){ const b = PC.boxes[PC.sel]; if (!b) return; $('#pcX').value = r3(b.position.x); $('#pcY').value = r3(b.position.y); $('#pcZ').value = r3(b.position.z); }
function pcSend(final){
  const b = PC.boxes[PC.sel]; if (!b || !PC.code) return; const now = performance.now();
  if (!final && now - PC.sendT < 80) return; PC.sendT = now;
  LiveT.move(PC.code, {i:b.userData.i, p:[r3(b.position.x), r3(b.position.y), r3(b.position.z)], seq:++PC.seq}).catch(e => { $('#pcSt').textContent = 'Envoi impossible'; console.warn(e); });
}
function pcClose(silent){
  if (!PC.code) return;
  cancelAnimationFrame(PC.raf); PC.unsub.forEach(f => f()); PC.unsub = [];
  try { PC.tc.detach(); PC.tc.dispose(); PC.orb.dispose(); PC.ren.dispose(); } catch(e){}
  PC.scene?.traverse(o => { o.geometry?.dispose?.(); o.material?.dispose?.(); });
  PC.code = null; PC.boxes = []; PC.sel = -1; $('#arPc').hidden = true; delete document.body.dataset.pc;
}
$('#pcUl').addEventListener('click', e => { const b = e.target.closest('[data-i]'); if (b) pcSelect(+b.dataset.i); });
$('#pcQ').addEventListener('input', pcList);
$('#pcClose').addEventListener('click', () => pcClose());
$('#pcBack').addEventListener('click', () => { const b = PC.boxes[PC.sel]; if (b){ b.position.set(...b.userData.p0); pcSync(); pcSend(true); } });
$('#pcReset').addEventListener('click', () => PC.boxes.forEach(b => { if (b.position.distanceTo(new THREE.Vector3(...b.userData.p0)) > 1e-4){ b.position.set(...b.userData.p0); LiveT.move(PC.code, {i:b.userData.i, p:b.userData.p0, seq:++PC.seq}); } }) || pcSync());
['X', 'Y', 'Z'].forEach((a, k) => $('#pc' + a).addEventListener('change', e => { const b = PC.boxes[PC.sel], v = parseFloat(e.target.value); if (b && Number.isFinite(v)){ b.position.setComponent(k, v); pcSend(true); } }));
$('#pcCanvas').addEventListener('pointerup', e => {
  if (PC.tc?.dragging || !PC.code || !PC.down) return;
  const cv = e.target.getBoundingClientRect(), n = new THREE.Vector2((e.clientX - cv.left) / cv.width * 2 - 1, -(e.clientY - cv.top) / cv.height * 2 + 1);
  if (Math.hypot(e.clientX - PC.down.x, e.clientY - PC.down.y) > 5) return;
  const rc = new THREE.Raycaster(); rc.setFromCamera(n, PC.cam); const h = rc.intersectObjects(PC.boxes, false)[0];
  if (h) pcSelect(h.object.userData.i);
});
$('#pcCanvas').addEventListener('pointerdown', e => { PC.down = {x:e.clientX, y:e.clientY}; });
$('#arPcBtn').addEventListener('click', pcAskCode);
window.AVLive = {openPc:pcAskCode, hostStart, hostStop, state:() => ({host:HOST.code, pc:PC.code, transport:LiveT.mode, objects:HOST.map.size}), pos:i => HOST.map.get(i)?.o.position.toArray()};
