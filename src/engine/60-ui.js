/* ============ Interface : panneau (Maquette, Données, Galerie, Infos), menus, captures, ouverture des maquettes ============ */
let activeTab = 'tree';
const pb = () => $('#pbody');
function setTab(t){ activeTab = t; $$('.tabs [data-tab]').forEach(b => b.setAttribute('aria-selected', b.dataset.tab === t)); renderPanel(); }
function openPanelTab(t){ setTab(t); if (isNarrow.matches){ $('#panel').classList.add('open'); $('#scrim').classList.add('on'); } }
$$('.tabs [data-tab]').forEach(b => b.addEventListener('click', () => setTab(b.dataset.tab)));
$('#panelBtn').addEventListener('click', () => { const p = $('#panel'); const o = !p.classList.contains('open'); p.classList.toggle('open', o); $('#scrim').classList.toggle('on', o); });
$('#scrim').addEventListener('click', () => { $('#panel').classList.remove('open'); $('#scrim').classList.remove('on'); });
let panelT = 0;
function refreshPanel(){ clearTimeout(panelT); panelT = setTimeout(renderPanel, 16); }
function renderPanel(){ ({tree:renderTree, props:renderProps, gallery:renderGalleryTab, info:renderInfo})[activeTab]?.(); }
window.addEventListener('av:select', e => { if (activeTab === 'props' || (activeTab === 'tree' && e.detail.from !== 'tree')){ if (activeTab === 'tree' && e.detail.obj){ for (let p = e.detail.obj.parent; p && p !== V.root; p = p.parent) treeOpen.add(keyOf(p)); } refreshPanel(); } });

/* clics du panneau */
pb().addEventListener('click', e => {
  const t = e.target;
  const tw = t.closest('[data-twist]'); if (tw){ const k = tw.dataset.twist; treeOpen.has(k) ? treeOpen.delete(k) : treeOpen.add(k); return renderTree(); }
  const ey = t.closest('[data-teye]'); if (ey){ const o = byKey(ey.dataset.teye); if (o){ setVisible(o, !o.visible); renderTree(); } return; }
  const tm = t.closest('[data-tmenu]'); if (tm){ e.stopPropagation(); const o = byKey(tm.dataset.tmenu); if (o) treeMenu(tm, o); return; }
  const ta = t.closest('[data-tact]')?.dataset.tact;
  if (ta === 'collapse'){ treeOpen.clear(); treeOpen.add(keyOf(V.root)); return renderTree(); }
  if (ta === 'expand'){ const add = o => kids(o).forEach(c => { if (treeOpen.has(keyOf(c))) add(c); else if (kids(c).length) treeOpen.add(keyOf(c)); }); treeOpen.add(keyOf(V.root)); add(V.root); return renderTree(); }
  if (ta === 'showall'){ showAll(); return renderTree(); }
  if (ta === 'fit') return setView('iso');
  const row = t.closest('.tr[data-tk]'); if (row){ const o = byKey(row.dataset.tk); if (o){ select(o === V.root ? null : o, {from:'tree'}); renderTree(); } return; }
  const pa = t.closest('[data-pact]')?.dataset.pact; if (pa && V.sel){ const o = V.sel;
    if (pa === 'focus') focusObject(o); else if (pa === 'isolate') isolate(o); else if (pa === 'hide'){ setVisible(o, false); select(null); } else if (pa === 'showall') showAll(); return; }
  const ph = t.closest('[data-photo]'); if (ph) return window.AVGallery?.open(ph.dataset.photo, galleryIds);
  const ia = t.closest('[data-iact]')?.dataset.iact;
  if (ia === 'capture') return doCapture();
  if (ia === 'reload') return reloadUnits();
});
pb().addEventListener('dblclick', e => { const row = e.target.closest('.tr[data-tk]'); if (row){ const o = byKey(row.dataset.tk); if (o && o !== V.root) focusObject(o); } });
pb().addEventListener('change', e => { if (e.target.id === 'unitSel'){ V.rec.unitScale = +e.target.value; reloadUnits(); } });

/* ---------- Galerie de la maquette ---------- */
let galleryIds = [];
async function renderGalleryTab(){
  const u = window.AVUser?.(); if (!u || !V.rec) return pb().innerHTML = '<p class="hint">Aucune maquette sélectionnée. Choisissez un fichier dans l\'onglet FICHIERS.</p>';
  pb().innerHTML = `<div class="ph"><h2>Galerie</h2><span class="tag">Maquette</span></div><p class="hint">Captures du chantier et de la maquette.</p><button class="btn primary wide-btn" data-iact="capture">Nouvelle capture (vue actuelle)</button><div class="pgrid" id="pgrid"><p class="hint">Chargement…</p></div>`;
  const ps = (await window.AVStore.listModelPhotos(u.uid, V.rec.id)).sort((a, b) => b.createdAt - a.createdAt);
  galleryIds = ps.map(p => p.id);
  const g = $('#pgrid'); if (!g || activeTab !== 'gallery') return;
  g.innerHTML = ps.length ? ps.map(p => `<button class="pthumb" data-photo="${p.id}"><img src="${esc(p.thumb)}" alt=""><small>${p.mode === 'ar' ? 'AR · ' : ''}${new Date(p.createdAt).toLocaleDateString('fr-FR', {day:'numeric', month:'short'})} ${new Date(p.createdAt).toLocaleTimeString('fr-FR', {hour:'2-digit', minute:'2-digit'})}${p.note ? ' · ✎' : ''}</small></button>`).join('') : '<p class="hint">Aucune photo pour le moment.</p>';
}
async function savePhoto(shot, mode){
  const u = window.AVUser?.(); if (!u) throw new Error('compte');
  const th = document.createElement('canvas'), k = 360 / shot.w; th.width = 360; th.height = Math.round(shot.h * k); th.getContext('2d').drawImage(shot.canvas, 0, 0, th.width, th.height);
  const rec = {id:window.AVStore.newId('p_'), uid:u.uid, modelId:V.rec?.id || 'sample', modelName:V.rec?.name || 'Maquette', blob:shot.blob, size:shot.blob.size, w:shot.w, h:shot.h, thumb:th.toDataURL('image/jpeg', 0.75), note:'', mode, createdAt:Date.now()};
  await window.AVStore.putPhoto(rec);
  if (activeTab === 'gallery') renderGalleryTab();
  return rec;
}
async function doCapture(){
  if (!V.root) return toast('Aucune maquette chargée. Choisissez un fichier dans l\'onglet FICHIERS.');
  try { const shot = await captureView({scale:2}); const r = await savePhoto(shot, 'tour'); flash(); toast('Capture enregistrée dans la galerie ArchiVision.'); return r; }
  catch(e){ console.error(e); toast('Échec de la capture.'); }
}
function flash(){ const f = document.createElement('div'); f.className = 'flash'; vpEl.append(f); setTimeout(() => f.remove(), 450); }

/* ---------- Infos de la maquette ---------- */
function renderInfo(){
  const r = V.rec; if (!r || !V.root) return pb().innerHTML = '<p class="hint">Aucune maquette chargée.</p>';
  const ap = V.root.userData?.archiplan, ip = V.root.userData?.ifcProjet;
  const KD = {ifc:'IFC (converti en GLB à l\'import)', glb:'GLB (glTF binaire)', gltf:'glTF', sample:'Maquette d\'exemple'};
  pb().innerHTML = `<div class="ph"><h2>${esc(r.name)}</h2><span class="tag">Infos</span></div>
    <div class="grp"><h3>Fichier</h3><dl class="kv">${kvRows([['Format', KD[r.kind] || r.kind], ['Taille d\'origine', r.size ? (r.size / 1e6).toLocaleString('fr-FR', {maximumFractionDigits:2}) + ' Mo' : null], ['Importée le', r.createdAt ? new Date(r.createdAt).toLocaleString('fr-FR', {dateStyle:'long', timeStyle:'short'}) : null],
      ['Projet IFC', ip?.nom], ['Schéma', ip?.schema], ['Projet ARCHIPLAN', ap?.projet], ['Version ARCHIPLAN', ap?.version && ap.version !== 'exemple' ? ap.version : null], ['Adresse', ap?.lieu?.adresse]])}</dl></div>
    <div class="grp"><h3>Maquette</h3><dl class="kv">${kvRows([['Dimensions (L × l × h)', `${nf(V.size.x, 2)} × ${nf(V.size.z, 2)} × ${nf(V.size.y, 2)} m`], ['Objets 3D', V.meshes.length], ['Triangles', Math.round(V.tris).toLocaleString('fr-FR')], ['Éléments BIM', countEls(V.root) || '—']])}</dl>
      <label class="f wide"><span>Unités du fichier</span><span class="in"><select id="unitSel"><option value="1"${V.unitScale === 1 ? ' selected' : ''}>Mètres</option><option value="0.01"${V.unitScale === 0.01 ? ' selected' : ''}>Centimètres</option><option value="0.001"${V.unitScale === 0.001 ? ' selected' : ''}>Millimètres</option></select></span></label>
      <p class="hint">1 = maquette en mètres. Si la maquette paraît géante ou minuscule en réalité augmentée, changez l'unité.</p></div>
    <div class="grp"><h3>Réalité augmentée</h3><dl class="kv">${kvRows([['Cet appareil', AR.supported ? 'WebXR (ARCore) disponible' : isIOS ? 'AR Quick Look (iPhone / iPad)' : AR.supported === false ? 'Non pris en charge' : 'Vérification…'], ['Échelle de pose', ratioLabel(AR.scale)]])}</dl></div>`;
}
async function reloadUnits(){ normalizeModel(V.rec.unitScale || undefined); setView('iso', true); if (V.rec.id && V.rec.kind !== 'sample') await window.AVStore.patchModel(V.rec.id, {unitScale:V.unitScale}).catch(() => {}); renderInfo(); $('#statOut').textContent = `${nf(V.size.x, 1)} × ${nf(V.size.z, 1)} × ${nf(V.size.y, 1)} m · ${V.meshes.length} objets`; }

/* ---------- Modes Visite 3D / Réalité augmentée ---------- */
function setModeButtons(){ $$('.modes [data-mode]').forEach(b => b.setAttribute('aria-pressed', (b.dataset.mode === 'ar') === (V.mode === 'ar'))); }
$$('.modes [data-mode]').forEach(b => b.addEventListener('click', () => { if (b.dataset.mode === 'ar') startAR(); else if (AR.session) AR.session.end(); }));
$('#arBtn').addEventListener('click', () => startAR());
$('#capBtn').addEventListener('click', () => doCapture());
$$('.tools [data-tool]').forEach(b => b.addEventListener('click', () => setTool(b.dataset.tool)));
$$('.tools [data-act]').forEach(b => b.addEventListener('click', () => b.dataset.act === 'fit' ? setView('iso') : doCapture()));
$$('#styleGrp [data-style]').forEach(b => b.addEventListener('click', () => setStyle(b.dataset.style)));
$$('.cams [data-cam]').forEach(b => b.addEventListener('click', () => setView(b.dataset.cam === 'fit' ? 'iso' : b.dataset.cam)));
$$('.status [data-opt]').forEach(b => b.addEventListener('click', () => {
  const k = b.dataset.opt; V.opts[k] = !V.opts[k]; b.setAttribute('aria-pressed', V.opts[k]);
  if (k === 'ground') ground.visible = V.opts.ground; renderer.shadowMap.enabled = true; refreshMaterials();
}));
$('#homeBtn').addEventListener('click', () => window.AVUI?.showHome());
$('#vpHome').addEventListener('click', () => window.AVUI?.showHome());

/* menu Fichier */
const fm = $('#fileMenu');
$('#fileBtn').addEventListener('click', e => { e.stopPropagation(); if (!fm.hidden){ fm.hidden = true; return; } fm.querySelector('.lock').hidden = canFull(); fm.hidden = false; const r = e.currentTarget.getBoundingClientRect(); fm.style.top = (r.bottom + 6) + 'px'; fm.style.left = Math.max(10, Math.min(r.left, innerWidth - fm.offsetWidth - 10)) + 'px'; });
document.addEventListener('click', e => { if (!fm.hidden && !fm.contains(e.target)) fm.hidden = true; });
fm.addEventListener('click', async e => {
  const a = e.target.closest('[data-act]')?.dataset.act; if (!a) return; fm.hidden = true;
  const base = (V.rec?.name || 'maquette').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^\w-]+/g, '_').slice(0, 60);
  if (a === 'home') return window.AVUI?.showHome();
  if (a === 'import'){ if (needLicence()) return; return $('#homeFile').click(); }
  if (a === 'sample') return openSample();
  if (!V.root) return toast('Aucune maquette chargée.');
  if (needLicence()) return;
  try {
    if (a === 'png'){ toast('Calcul de l\'image…'); const s = await captureView({scale:3, mime:'image/png'}); return window.AVUI.download(`${base}_vue.png`, s.blob); }
    if (a === 'glb'){ toast('Préparation du GLB…'); return window.AVUI.download(`${base}.glb`, await exportGLB(V.root)); }
    if (a === 'usdz'){ toast('Préparation du USDZ…'); return window.AVUI.download(`${base}.usdz`, await makeUSDZ()); }
  } catch(err){ console.error(err); toast('L\'export a échoué. Réessayez dans un instant.'); }
});

/* ---------- Ouverture ---------- */
async function open(id, opts = {}){
  if (AR.session) AR.session.end();
  if (!(V.rec?.id === id && V.root)){
    loading(true, 'Ouverture de la maquette', 'Lecture sur l\'appareil…', 0.2);
    try {
      const r = await window.AVStore.getModel(id); if (!r) throw new Error('Maquette introuvable sur cet appareil.');
      loading(true, null, 'Chargement de la 3D…', 0.5); await frame();
      const root = await parseGLB(await r.glb.arrayBuffer());
      const {glb, source, ...meta} = r; setModel(root, meta);
      window.AVStore.patchModel(id, {openedAt:Date.now()}).catch(() => {});
    } finally { loading(false); }
  }
  afterOpen(opts);
}
async function openSample(opts = {}){
  if (AR.session) AR.session.end();
  if (V.rec?.id !== 'sample'){ setModel(buildSample(), {id:'sample', name:'Maison d\'exemple', kind:'sample', createdAt:0}); }
  window.AVUI?.hideHome(); afterOpen(opts);
}
function afterOpen(opts){
  $('#projName').textContent = V.rec?.name || 'Maquette';
  treeOpen.clear(); treeQ = ''; setTool('orbit'); setTab(activeTab === 'info' ? 'info' : 'tree'); setModeButtons(); resize(); setView('iso', true);
  if (opts.ar){ if (AR.supported || isIOS) toast('Touchez « Voir en AR » pour poser la maquette.'); else arFallback(); $('#arBar').classList.add('pulse'); setTimeout(() => $('#arBar').classList.remove('pulse'), 3200); }
}
function leave(){ if (AR.session) AR.session.end(); }

/* ---------- API pour l'accueil (script classique) ---------- */
window.AVApp = {open, openSample, importFiles, leave, refreshGallery:() => { if (activeTab === 'gallery') renderGalleryTab(); }, state:() => ({tool:V.tool, mode:V.mode, style:V.style, name:V.rec?.name, meshes:V.meshes.length, tris:V.tris, size:V.size.toArray(), sel:V.sel ? objLabel(V.sel) : null, ar:AR.supported})};
window.AVDebug = {V, AR, scene, camera, controls, select, elementOf, pickAt, setTool, setView, setStyle, objLabel, captureView, setSection, measures};
setTool('orbit'); setTab('tree'); $('#vpEmpty').hidden = false; prompt(TOOL_HINT.orbit);
window.__apReady = true;
window.dispatchEvent(new Event('ap:ready'));
/* fin de l'intro : retirée ici aussi quand la session est restaurée sans passer par l'écran de compte (même principe qu'ARCHITRAFIC) */
(() => { const sp = document.getElementById('splash'); if (!sp) return; const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  setTimeout(() => { sp.classList.add('done'); setTimeout(() => sp.remove(), 600); }, Math.max(reduce ? 300 : 0, (reduce ? 600 : 3000) - performance.now())); })();
