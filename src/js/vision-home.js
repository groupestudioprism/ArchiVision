/* =====================================================================
   ARCHIVISION — accueil « Mes maquettes » et « Galerie », stockage sur l'appareil (IndexedDB).
   Remplace le cloud d'ARCHIPLAN / ARCHITRAFIC (cloud.js) : les maquettes (GLB, IFC converti) et les photos
   peuvent peser plusieurs dizaines de Mo, elles restent sur l'appareil, rangées par compte Studio Prism.
   Fournit à auth.js les mêmes points d'entrée que cloud.js (showHome, renderHome, Save, dlg…).
   ===================================================================== */
const withTimeout = (p, ms, code = 'app/timeout') => Promise.race([p, new Promise((_, rej) => setTimeout(() => rej({code}), ms))]);
async function sha256hex(s){ const h = await crypto.subtle.digest('SHA-256', enc.encode(s)); return [...new Uint8Array(h)].map(b => b.toString(16).padStart(2, '0')).join(''); }
const fullAccess = () => access.level === 'admin' || access.level === 'licensed';
const fullName = () => `${me?.firstName || ''} ${me?.lastName || ''}`.trim() || me?.email || '';
const mb = n => (n / 1e6).toLocaleString('fr-FR', {maximumFractionDigits:n < 1e7 ? 1 : 0}) + ' Mo';
const kb = n => n > 1e6 ? (n / 1e6).toFixed(1).replace('.', ',') + ' Mo' : Math.max(1, Math.round((n || 0) / 1000)) + ' Ko';
const ago = t => { if (!t) return ''; const s = (Date.now() - t) / 1000; if (s < 60) return 'à l\'instant'; if (s < 3600) return `il y a ${Math.round(s / 60)} min`; if (s < 86400) return `il y a ${Math.round(s / 3600)} h`;
  return new Date(t).toLocaleDateString('fr-FR', {day:'numeric', month:'short', year:new Date(t).getFullYear() === new Date().getFullYear() ? undefined : 'numeric'}); };
const safeName = s => (String(s || 'maquette').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^\w-]+/g, '-').replace(/^-+|-+$/g, '') || 'maquette').slice(0, 60);

/* Pas de projets cloud dans ARCHIVISION : objets attendus par auth.js, sans effet */
const Save = {pid:null, pending:() => false, flush:async () => {}, end(){}, set(){}, canWrite:() => true};
const H = {tab:'mine', folder:'', mine:null, shared:null, models:null, photos:null, loading:false, err:'', usage:null, quota:null};
function loadUserLib(){}
function readLinkFromUrl(){ return null; }
async function openPendingLink(){}
async function newProject(){ $a('#homeFile').click(); }

/* ---------- Stockage sur l'appareil ---------- */
const AVStore = (() => {
  let dbp = null;
  const open = () => dbp || (dbp = new Promise((res, rej) => {
    const r = indexedDB.open('archivision', 1);
    r.onupgradeneeded = () => {
      const db = r.result;
      const m = db.createObjectStore('models', {keyPath:'id'}); m.createIndex('uid', 'uid');
      const p = db.createObjectStore('photos', {keyPath:'id'}); p.createIndex('uid', 'uid'); p.createIndex('modelId', 'modelId');
    };
    r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error);
  }));
  const tx = async (store, mode, fn) => { const db = await open(); return new Promise((res, rej) => { const t = db.transaction(store, mode), s = t.objectStore(store); let out; Promise.resolve(fn(s)).then(v => out = v); t.oncomplete = () => res(out); t.onerror = () => rej(t.error); t.onabort = () => rej(t.error || {code:'app/storage'}); }); };
  const req = r => new Promise((res, rej) => { r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error); });
  const byUid = (store, uid) => tx(store, 'readonly', s => req(s.index('uid').getAll(uid)));
  const id = p => p + Date.now().toString(36) + Math.random().toString(36).slice(2, 7);
  return {
    newId:id,
    async listModels(uid){ const all = await byUid('models', uid); return all.map(({glb, source, ...m}) => ({...m, glbSize:glb?.size || 0, sourceSize:source?.size || 0})); },
    getModel:(mid) => tx('models', 'readonly', s => req(s.get(mid))),
    putModel:(m) => tx('models', 'readwrite', s => req(s.put(m))),
    async patchModel(mid, patch){ const m = await this.getModel(mid); if (!m) return; Object.assign(m, patch); await this.putModel(m); return m; },
    async deleteModel(mid){ await tx('models', 'readwrite', s => req(s.delete(mid))); const ps = await tx('photos', 'readonly', s => req(s.index('modelId').getAll(mid))); await tx('photos', 'readwrite', s => Promise.all(ps.map(p => req(s.delete(p.id))))); },
    listPhotos:(uid) => byUid('photos', uid),
    async listModelPhotos(uid, mid){ return (await tx('photos', 'readonly', s => req(s.index('modelId').getAll(mid)))).filter(p => p.uid === uid); },
    getPhoto:(pid) => tx('photos', 'readonly', s => req(s.get(pid))),
    putPhoto:(p) => tx('photos', 'readwrite', s => req(s.put(p))),
    deletePhoto:(pid) => tx('photos', 'readwrite', s => req(s.delete(pid))),
    async estimate(){ try { const e = await navigator.storage?.estimate?.(); return e || null; } catch(e){ return null; } },
    async persist(){ try { if (await navigator.storage?.persisted?.()) return true; return !!(await navigator.storage?.persist?.()); } catch(e){ return false; } },
  };
})();
window.AVStore = AVStore;
window.AVUser = () => me ? {uid:me.uid, name:fullName()} : null;
window.AVAccess = () => fullAccess();

/* ---------- Pont avec le moteur 3D (module) ---------- */
let modelReady = window.AVApp ? Promise.resolve() : new Promise(res => window.addEventListener('ap:ready', res, {once:true}));
const engine = async () => { await withTimeout(modelReady, 20000, 'app/engine'); return window.AVApp; };
function hideHome(){ $a('#home').hidden = true; const app = $a('#app'); if (app) app.inert = false; ctx.where = 'app'; }
window.AVUI = {
  hideHome, showHome:() => showHome(), toast:m => toastA(m), confirm:(t, x, ok, fn) => confirmDlg(t, x, ok, fn), dlg:(h, w) => dlg(h, w), closeDlg:() => closeDlg(),
  licence:reason => show('licence', {reason}), refreshHome:() => { if (ctx.where === 'home') loadHome(); }, esc,
};
async function openModel(m, opts = {}){
  showBusy(`Ouverture de « ${m.name} »…`);
  try { const E = await engine(); await E.open(m.id, opts); hideBusy(); hideHome(); }
  catch(e){ hideBusy(); toastA(e?.code === 'app/engine' ? 'Le moteur 3D n\'est pas encore chargé. Réessayez.' : e?.message || 'Impossible d\'ouvrir la maquette.'); }
}
async function openSample(opts = {}){
  showBusy('Préparation de la maquette d\'exemple…');
  try { const E = await engine(); await E.openSample(opts); hideBusy(); hideHome(); } catch(e){ hideBusy(); toastA(e?.message || 'Exemple indisponible.'); }
}

/* ---------- Accueil ---------- */
const ICON = {
  dots:'<svg viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="5" r="1.8"/><circle cx="12" cy="12" r="1.8"/><circle cx="12" cy="19" r="1.8"/></svg>',
  model:'<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="1.4"><path d="M24 6l15 8v20l-15 8-15-8V14z"/><path d="M9 14l15 8 15-8M24 22v20" opacity=".6"/></svg>',
  photo:'<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="1.4"><path d="M8 15h7l4-5h10l4 5h7v23H8z"/><circle cx="24" cy="26" r="7"/></svg>',
};
const KIND = {glb:'GLB', gltf:'glTF', ifc:'IFC', zip:'ZIP'};
function showHome(){
  try { window.AVApp?.leave?.(); } catch(e){}
  ctx.where = 'home'; const h = $a('#home'); h.hidden = false; $a('#app').inert = true;
  const hu = $a('#homeUser'); hu.textContent = initials(); hu.title = fullName();
  const hour = new Date().getHours(); $a('#homeHello').textContent = `${hour < 18 && hour > 4 ? 'Bonjour' : 'Bonsoir'} ${me?.firstName || ''}`.trim();
  renderHome(); loadHome();
}
async function loadHome(){
  if (!me) return;
  H.loading = true; H.err = ''; renderHome();
  try {
    const uid = me.uid;
    const [models, photos, est] = await Promise.all([AVStore.listModels(uid), AVStore.listPhotos(uid), AVStore.estimate()]);
    if (me?.uid !== uid) return;
    H.models = models.sort((a, b) => (b.openedAt || b.createdAt) - (a.openedAt || a.createdAt));
    H.photos = photos.sort((a, b) => b.createdAt - a.createdAt);
    H.usage = models.reduce((s, m) => s + (m.glbSize || 0) + (m.sourceSize || 0), 0) + photos.reduce((s, p) => s + (p.size || 0), 0);
    H.quota = est;
    H.persisted = await (navigator.storage?.persisted?.() ?? Promise.resolve(false)).catch(() => false);
  } catch(e){ H.err = 'Le stockage de l\'appareil est inaccessible (navigation privée ?). Les maquettes ne pourront pas être gardées.'; }
  H.loading = false; renderHome();
}
function renderQuota(){
  const q = $a('#homeQuota'); if (!q) return;
  const used = H.usage ?? 0, cap = H.quota?.quota || 0, pct = cap ? Math.min(100, (H.quota.usage || used) / cap * 100) : 0;
  const lab = access.level === 'admin' ? 'Administrateur' : fullAccess() ? 'Licence ARCHIVISION' : 'Découverte';
  q.innerHTML = `<div class="q-h"><span>Sur cet appareil</span><b class="q-plan ${fullAccess() ? 'pro' : 'free'}">${esc(lab)}</b></div>
    <div class="q-num"><b>${used < 1e6 ? kb(used) : mb(used)}</b>${cap ? ` / ${cap > 5e9 ? Math.round(cap / 1e9) + ' Go' : mb(cap)} disponibles` : ''}</div>
    <div class="q-bar" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${Math.round(pct)}"><i style="width:${Math.max(pct, used ? 1 : 0).toFixed(1)}%" class="${pct > 90 ? 'full' : pct > 70 ? 'mid' : ''}"></i></div>
    <dl class="q-dl"><dt>Maquettes</dt><dd>${H.models?.length ?? '—'}</dd><dt>Photos</dt><dd>${H.photos?.length ?? '—'}</dd><dt>Stockage</dt><dd>${H.persisted ? 'protégé' : 'standard'}</dd></dl>
    ${H.persisted ? '' : `<button type="button" class="q-up" data-hact="persist">Protéger le stockage</button>`}`;
}
function renderOffer(){
  const el = $a('#homeOffer'); if (!el) return;
  const hide = fullAccess() || access.level === 'pending' || !me || store.get('av.offerHide.' + me.uid);
  el.hidden = !!hide; if (hide) return;
  el.innerHTML = `<div class="ho-in"><div><b>Mode découverte</b><span>Visitez la maquette d'exemple en 3D. L'import de vos maquettes, la réalité augmentée et les exports demandent une licence ARCHIVISION.</span></div>
    <div class="ho-a"><a class="ho-b" href="${OFFERS_URL}" target="_blank" rel="noopener">Voir les offres</a><button type="button" class="ho-b" data-hact="key">J'ai une clé de licence</button><button type="button" class="ho-x" data-hact="offer-hide" aria-label="Masquer">✕</button></div></div>`;
}
function cardHtml(m){
  const thumb = m.thumb ? `<img src="${esc(m.thumb)}" alt="" loading="lazy">` : ICON.model;
  const np = (H.photos || []).filter(p => p.modelId === m.id).length;
  const sub = `${KIND[m.kind] || 'GLB'} · ${kb((m.sourceSize || 0) || m.glbSize)} · ${ago(m.openedAt || m.createdAt)}${np ? ` · ${np} photo${np > 1 ? 's' : ''}` : ''}`;
  return `<article class="hcard" data-open="${esc(m.id)}" tabindex="0" aria-label="Ouvrir ${esc(m.name)}">
    <div class="hthumb">${thumb}<span class="hkind k-${esc(m.kind)}">${KIND[m.kind] || 'GLB'}</span></div>
    <div class="hmeta"><b title="${esc(m.name)}">${esc(m.name)}</b><span>${sub}</span></div>
    <button type="button" class="hdots" data-menu="${esc(m.id)}" aria-label="Gérer ${esc(m.name)}">${ICON.dots}</button></article>`;
}
function photoHtml(p){
  return `<button type="button" class="gthumb" data-photo="${esc(p.id)}" aria-label="Photo du ${new Date(p.createdAt).toLocaleString('fr-FR')}"><img src="${esc(p.thumb || '')}" alt="" loading="lazy">
    <span><b>${esc(p.modelName || 'Maquette')}</b><small>${p.mode === 'ar' ? 'AR · ' : ''}${ago(p.createdAt)}${p.note ? ' · annotée' : ''}</small></span></button>`;
}
function renderHome(){
  const body = $a('#homeBody'); if (!body) return;
  $a('#homeTabs').querySelectorAll('[data-htab]').forEach(b => b.setAttribute('aria-selected', b.dataset.htab === H.tab));
  const lb = $a('#homeLic'); if (lb) lb.hidden = fullAccess() || access.level === 'pending';
  renderQuota(); renderOffer();
  const last = H.models?.[0]; const ars = $a('#homeARsub'); if (ars) ars.textContent = last ? last.name : 'Maquette d\'exemple';
  const sub = $a('#homeSub'); if (sub) sub.textContent = H.tab === 'mine' ? 'Vos maquettes BIM, à visiter en 3D ou à poser en réalité augmentée sur le chantier.' : 'Captures du chantier et de la maquette, avec vos annotations.';
  if (H.loading && !H.models){ body.innerHTML = `<div class="hgrid">${'<div class="hcard sk"><div class="hthumb"></div><div class="hmeta"><b></b><span></span></div></div>'.repeat(4)}</div>`; return; }
  if (H.err){ body.innerHTML = `<div class="hempty"><p>${esc(H.err)}</p><button type="button" class="ap-btn alt" data-hact="reload">Réessayer</button></div>`; return; }
  if (H.tab === 'mine'){
    const ms = H.models || [];
    body.innerHTML = ms.length ? `<h2 class="hsec">Maquettes récentes</h2><div class="hgrid">${ms.map(cardHtml).join('')}</div>
      <p class="hint h-note">Les maquettes sont gardées sur cet appareil, pour votre compte. Les fichiers IFC sont convertis une seule fois, à l'import.</p>`
      : `<div class="hempty"><div class="hic">${ICON.model}</div><h3>Aucune maquette trouvée.</h3><p>Importez un fichier GLB, glTF ou IFC pour commencer (export « Archi Vision » d'ARCHIPLAN, Revit, ArchiCAD, SketchUp…), ou ouvrez la maquette d'exemple.</p>
        <div class="hempty-a"><button type="button" class="ap-btn" data-hact="import">Importer une maquette</button><button type="button" class="ap-btn alt" data-hact="sample">Maquette d'exemple</button></div></div>`;
  } else {
    const ps = H.photos || [];
    body.innerHTML = ps.length ? `<h2 class="hsec">Captures du chantier et de la maquette</h2><div class="ggrid">${ps.map(photoHtml).join('')}</div>`
      : `<div class="hempty"><div class="hic">${ICON.photo}</div><h3>Aucune photo pour le moment.</h3><p>Pendant la visite 3D ou en réalité augmentée, touchez « Capture » : la vue est enregistrée ici, avec vos annotations.</p></div>`;
  }
}
function showBusy(t){ const b = $a('#homeBusy'); b.querySelector('span').textContent = t; b.hidden = false; }
function hideBusy(){ $a('#homeBusy').hidden = true; }
window.AVUI.busy = t => t ? showBusy(t) : hideBusy();

/* Import (bouton de l'accueil ou menu Fichier) */
async function importFiles(files){
  if (!files?.length) return;
  if (!fullAccess()){ show('licence', {reason:'export'}); return; }
  showBusy('Lecture du fichier…');
  try {
    const E = await engine();
    const rec = await E.importFiles([...files], t => showBusy(t));
    hideBusy(); toastA(`Importé : ${rec.name}`); await loadHome();
    await openModel(rec);
  } catch(e){ hideBusy(); console.error(e); toastA(e?.message ? `Import impossible : ${e.message}` : 'Import impossible.'); }
}
window.AVUI.importFiles = importFiles;
$a('#homeFile').addEventListener('change', e => { const f = e.target.files; importFiles(f).finally(() => { e.target.value = ''; }); });

/* Boîtes de dialogue (mêmes que l'accueil d'ARCHIPLAN) */
function dlg(html, wire){
  const d = $a('#apDlg'); d.querySelector('.dlg-card').innerHTML = html; d.hidden = false;
  d.querySelectorAll('[data-dlg="close"]').forEach(b => b.onclick = closeDlg);
  const f = d.querySelector('input:not([type=checkbox]),select'); if (f) setTimeout(() => f.focus(), 40);
  wire && wire(d);
}
function closeDlg(){ $a('#apDlg').hidden = true; }
$a('#apDlg').addEventListener('click', e => { if (e.target.id === 'apDlg') closeDlg(); });
function askText(title, label, value, okLabel, onOk){
  dlg(`<h3>${esc(title)}</h3><form id="dlgF"><label class="ap-field"><span>${esc(label)}</span><span class="ap-in"><input id="dlgIn" maxlength="120" value="${esc(value || '')}"></span></label><p class="ap-err" id="dlgErr"></p>
    <div class="dlg-acts"><button type="button" class="ap-btn alt" data-dlg="close">Annuler</button><button class="ap-btn" type="submit">${esc(okLabel)}</button></div></form>`, d => {
    const i = d.querySelector('#dlgIn'); i.select();
    d.querySelector('#dlgF').onsubmit = async e => { e.preventDefault(); const v = i.value.trim(); if (!v) return d.querySelector('#dlgErr').textContent = 'Indiquez un nom.';
      const b = d.querySelector('[type=submit]'); busy(b, true); try { await onOk(v); closeDlg(); } catch(err){ busy(b, false); d.querySelector('#dlgErr').textContent = err?.message || errMsg(err); } };
  });
}
function confirmDlg(title, text, okLabel, onOk){
  dlg(`<h3>${esc(title)}</h3><p class="dlg-p">${text}</p><p class="ap-err" id="dlgErr"></p><div class="dlg-acts"><button type="button" class="ap-btn alt" data-dlg="close">Annuler</button><button type="button" class="ap-btn danger" id="dlgOk">${esc(okLabel)}</button></div>`, d => {
    d.querySelector('#dlgOk').onclick = async e => { busy(e.currentTarget, true); try { await onOk(); closeDlg(); } catch(err){ busy(e.currentTarget, false); d.querySelector('#dlgErr').textContent = err?.message || errMsg(err); } };
  });
}

/* Menu ⋮ d'une maquette */
const hm = () => $a('#homeMenu');
function openMenu(anchor, items){
  const m = hm(); m.innerHTML = items.map((it, i) => it === '-' ? '<hr>' : `<button type="button" data-i="${i}"${it.danger ? ' class="danger"' : ''}><b>${esc(it.label)}</b>${it.sub ? `<span>${esc(it.sub)}</span>` : ''}</button>`).join('');
  m.hidden = false; const r = anchor.getBoundingClientRect();
  m.style.top = Math.min(r.bottom + 4, innerHeight - m.offsetHeight - 10) + 'px'; m.style.left = Math.max(10, Math.min(r.right - m.offsetWidth, innerWidth - m.offsetWidth - 10)) + 'px';
  m.onclick = e => { const b = e.target.closest('[data-i]'); if (!b) return; m.hidden = true; items[+b.dataset.i].run(); };
}
document.addEventListener('click', e => { const m = hm(); if (!m.hidden && !m.contains(e.target) && !e.target.closest('[data-menu]')) m.hidden = true; });
async function downloadBlob(filename, blob){
  try { const dl = window.claude?.use ? await window.claude.use('downloads') : null; if (dl){ await dl.save({filename, data:new Uint8Array(await blob.arrayBuffer())}); return; } } catch(e){ if (e?.code === 'declined') return; }
  const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = filename; document.body.appendChild(a); a.click();
  setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 2000);
}
window.AVUI.download = downloadBlob;
function modelMenu(btn, m){
  openMenu(btn, [
    {label:'Ouvrir la visite 3D', sub:'Orbite, marche, immersion 360°', run:() => openModel(m)},
    {label:'Voir en réalité augmentée', sub:'Poser la maquette sur le sol ou une table', run:() => openModel(m, {ar:true})},
    {label:'Renommer', run:() => askText('Renommer la maquette', 'Nom', m.name, 'Renommer', async v => { await AVStore.patchModel(m.id, {name:v}); await loadHome(); })},
    {label:'Télécharger (.glb)', sub:m.kind === 'ifc' ? 'Version convertie, avec les données IFC' : 'Fichier de la maquette', run:async () => {
      if (!fullAccess()) return show('licence', {reason:'export'});
      const r = await AVStore.getModel(m.id); if (r?.glb) await downloadBlob(safeName(m.name) + '.glb', r.glb); }},
    ...(m.kind === 'ifc' ? [{label:'Télécharger l\'IFC d\'origine', run:async () => { if (!fullAccess()) return show('licence', {reason:'export'}); const r = await AVStore.getModel(m.id); if (r?.source) await downloadBlob(safeName(m.name) + '.ifc', r.source); }}] : []),
    '-',
    {label:'Supprimer', sub:'La maquette et ses photos, sur cet appareil', danger:true, run:() => confirmDlg('Supprimer ?', `« ${esc(m.name)} » et ses ${(H.photos || []).filter(p => p.modelId === m.id).length} photo(s) seront effacées de cet appareil. Cette action est définitive.`, 'Supprimer', async () => { await AVStore.deleteModel(m.id); toastA(`Supprimé : ${m.name}`); await loadHome(); })},
  ]);
}

/* Clics de l'accueil */
$a('#homeTabs').addEventListener('click', e => { const b = e.target.closest('[data-htab]'); if (!b) return; H.tab = b.dataset.htab; renderHome(); });
$a('#homeNewP').addEventListener('click', () => { if (!fullAccess()) return show('licence', {reason:'export'}); $a('#homeFile').click(); });
$a('#homeSample').addEventListener('click', () => openSample());
$a('#homeAR').addEventListener('click', () => { const m = H.models?.[0]; m ? openModel(m, {ar:true}) : openSample({ar:true}); });
$a('#homeAP').addEventListener('click', () => dlg(`<h3>Une maquette d'ARCHIPLAN</h3>
  <ol class="dlg-ol"><li>Dans ARCHIPLAN, ouvrez votre projet puis <b>Exporter</b> → <b>Archi Vision (.glb)</b>, ou le <b>paquet .zip</b> (GLB + IFC + métré).</li>
  <li>Ici, touchez <b>Importer</b> et choisissez le fichier <code>.glb</code>, <code>.ifc</code> ou <code>.zip</code>.</li>
  <li>Les niveaux, éléments, compositions de parois, U et familles d'ARCHIPLAN apparaissent dans l'onglet <b>Données</b>.</li></ol>
  <div class="dlg-acts"><a class="ap-btn alt" href="https://groupestudioprism.github.io/ArchiPlan/" target="_blank" rel="noopener" style="text-decoration:none">Ouvrir ARCHIPLAN ↗</a><button type="button" class="ap-btn" id="dlgImp">Importer un fichier</button></div>`, d => {
  d.querySelector('#dlgImp').onclick = () => { closeDlg(); if (!fullAccess()) return show('licence', {reason:'export'}); $a('#homeFile').click(); }; }));
$a('#homeLic').addEventListener('click', () => show('licence'));
$a('#home').addEventListener('click', async e => {
  const t = e.target;
  const mb_ = t.closest('[data-menu]'); if (mb_){ e.stopPropagation(); const m = H.models?.find(x => x.id === mb_.dataset.menu); if (m) modelMenu(mb_, m); return; }
  const card = t.closest('[data-open]'); if (card){ const m = H.models?.find(x => x.id === card.dataset.open); if (m) openModel(m); return; }
  const ph = t.closest('[data-photo]'); if (ph){ AVGallery.open(ph.dataset.photo, (H.photos || []).map(p => p.id)); return; }
  const a = t.closest('[data-hact]')?.dataset.hact; if (!a) return;
  if (a === 'reload') loadHome();
  else if (a === 'import'){ if (!fullAccess()) return show('licence', {reason:'export'}); $a('#homeFile').click(); }
  else if (a === 'sample') openSample();
  else if (a === 'key') show('licence');
  else if (a === 'offer-hide'){ store.set('av.offerHide.' + me.uid, true); renderOffer(); }
  else if (a === 'persist'){ const ok = await AVStore.persist(); toastA(ok ? 'Stockage protégé : le navigateur ne l\'effacera pas pour faire de la place.' : 'Le navigateur a refusé. Installez l\'app sur l\'écran d\'accueil pour protéger le stockage.'); loadHome(); }
});
$a('#home').addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.matches('[data-open]')) e.target.click(); });

/* ---------- Visionneuse de photos (galerie), annotations ---------- */
const AVGallery = {
  ids:[], cur:null, url:'',
  async open(id, ids){
    this.ids = ids && ids.length ? ids : [id]; const p = await AVStore.getPhoto(id); if (!p) return toastA('Aucune photo ouverte.');
    this.cur = p; if (this.url) URL.revokeObjectURL(this.url); this.url = URL.createObjectURL(p.blob);
    const i = this.ids.indexOf(id), d = $a('#photoDlg');
    d.querySelector('.ph-card').innerHTML = `<div class="ph-img"><img id="photo-preview" src="${this.url}" alt="Capture">
        ${this.ids.length > 1 ? `<button class="ph-nav prev" data-ph="prev" ${i <= 0 ? 'disabled' : ''} aria-label="Photo précédente">‹</button><button class="ph-nav next" data-ph="next" ${i >= this.ids.length - 1 ? 'disabled' : ''} aria-label="Photo suivante">›</button>` : ''}</div>
      <div class="ph-side"><div class="ph-h"><div><b>${esc(p.modelName || 'Maquette')}</b><small>${new Date(p.createdAt).toLocaleString('fr-FR', {dateStyle:'long', timeStyle:'short'})} · ${p.mode === 'ar' ? 'réalité augmentée' : 'visite 3D'} · ${p.w}×${p.h}</small></div><button class="ib ghost" data-ph="close" aria-label="Fermer">✕</button></div>
        <label class="ph-note"><span>Annotation</span><textarea id="phNote" rows="6" placeholder="Observation, réserve, cote à vérifier…">${esc(p.note || '')}</textarea></label>
        <div class="ph-acts"><button class="ap-btn" data-ph="save">Enregistrer la note</button><button class="ap-btn alt" data-ph="export">Enregistrer dans le téléphone</button><button class="ap-btn alt danger-t" data-ph="delete">Supprimer</button></div></div>`;
    d.hidden = false;
  },
  close(){ $a('#photoDlg').hidden = true; if (this.url){ URL.revokeObjectURL(this.url); this.url = ''; } this.cur = null; },
  async act(a){
    const p = this.cur; if (!p) return;
    if (a === 'close') return this.close();
    if (a === 'prev' || a === 'next'){ const i = this.ids.indexOf(p.id) + (a === 'next' ? 1 : -1); if (this.ids[i]) this.open(this.ids[i], this.ids); return; }
    if (a === 'save'){ const note = $a('#phNote').value.trim(); if (!note && !p.note) return toastA('Aucune annotation à enregistrer.'); p.note = note; await AVStore.putPhoto(p); toastA('Annotation enregistrée.'); refreshAfterPhoto(); return; }
    if (a === 'export'){
      const name = `${safeName(p.modelName)}_${new Date(p.createdAt).toISOString().slice(0, 16).replace(/[:T]/g, '-')}.jpg`;
      const blob = p.note ? await annotateImage(p) : p.blob;
      const file = new File([blob], name, {type:blob.type || 'image/jpeg'});
      try { if (navigator.canShare?.({files:[file]})){ await navigator.share({files:[file], title:'ARCHIVISION', text:p.note || ''}); return; } } catch(e){ if (e?.name === 'AbortError') return; }
      await downloadBlob(name, blob); toastA('Photo enregistrée dans les téléchargements.'); return;
    }
    if (a === 'delete') return confirmDlg('Supprimer ?', 'Cette photo et son annotation seront effacées de cet appareil.', 'Supprimer', async () => { await AVStore.deletePhoto(p.id); this.ids = this.ids.filter(x => x !== p.id); this.close(); toastA('Photo supprimée.'); refreshAfterPhoto(); });
  },
};
window.AVGallery = AVGallery;
function refreshAfterPhoto(){ if (ctx.where === 'home') loadHome(); try { window.AVApp?.refreshGallery?.(); } catch(e){} }
$a('#photoDlg').addEventListener('click', e => { if (e.target.id === 'photoDlg') return AVGallery.close(); const b = e.target.closest('[data-ph]'); if (b) AVGallery.act(b.dataset.ph); });
window.addEventListener('keydown', e => { if ($a('#photoDlg').hidden) return; e.stopPropagation(); if (e.target.closest?.('textarea')) return; if (e.key === 'Escape') AVGallery.close(); if (e.key === 'ArrowRight') AVGallery.act('next'); if (e.key === 'ArrowLeft') AVGallery.act('prev'); }, true);
/* Export : l'annotation est écrite dans un bandeau sous l'image */
async function annotateImage(p){
  const img = await createImageBitmap(p.blob), W = img.width, pad = Math.round(W / 40), fs = Math.max(14, Math.round(W / 55));
  const c = document.createElement('canvas'), g = c.getContext('2d'); g.font = `500 ${fs}px Inter, sans-serif`;
  const lines = []; for (const para of p.note.split('\n')){ let l = ''; for (const w of para.split(' ')){ const t = l ? l + ' ' + w : w; if (g.measureText(t).width > W - 2 * pad && l){ lines.push(l); l = w; } else l = t; } lines.push(l); }
  const band = pad * 2 + fs * 1.4 * (lines.length + 1);
  c.width = W; c.height = img.height + band; g.drawImage(img, 0, 0);
  g.fillStyle = '#0B1F45'; g.fillRect(0, img.height, W, band);
  g.fillStyle = '#7FE0CF'; g.font = `700 ${fs}px Montserrat, Inter, sans-serif`; g.fillText(`ARCHIVISION · ${p.modelName || ''} · ${new Date(p.createdAt).toLocaleString('fr-FR')}`, pad, img.height + pad + fs);
  g.fillStyle = '#FFFFFF'; g.font = `500 ${fs}px Inter, sans-serif`; lines.forEach((l, i) => g.fillText(l, pad, img.height + pad + fs * (2.4 + i * 1.4)));
  return new Promise(r => c.toBlob(r, 'image/jpeg', .9));
}
