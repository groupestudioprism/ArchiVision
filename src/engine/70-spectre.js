/* ============ Clic droit partout + Spectre (Ctrl K) ============
   Même mécanique que la gamme Spectra : CTX.zone(sélecteur, fonction) déclare les actions d'une zone (la première qui répond l'emporte),
   CTX.open(x, y, entrées) affiche le menu (sous-menus, clavier, appui long sur tactile). Les champs de saisie gardent le menu du navigateur.
   Le Spectre est une palette de commandes : Ctrl K (⌘ K sur Mac), tapez quelques lettres, Entrée. */
const CTX = {
  zones:[], stack:[], res:null,
  zone(sel, fn){ this.zones.push({sel, fn}); },
  init(){
    document.addEventListener('contextmenu', e => this.onCtx(e));
    document.addEventListener('pointerdown', e => { if (this.stack.length && !e.target.closest('.ctx')) this.close(); }, true);
    window.addEventListener('blur', () => this.close()); window.addEventListener('resize', () => this.close());
    document.addEventListener('wheel', e => { if (this.stack.length && !e.target.closest('.ctx')) this.close(); }, {passive:true, capture:true});
    window.addEventListener('keydown', e => this.key(e), true);
    // clic droit maintenu et déplacé sur la vue 3D = panoramique : pas de menu au relâchement
    let rd = null;
    document.addEventListener('pointerdown', e => { rd = e.button === 2 ? {x:e.clientX, y:e.clientY} : null; }, true);
    document.addEventListener('pointerup', e => { if (rd && Math.hypot(e.clientX - rd.x, e.clientY - rd.y) > 5) CTX.skipUntil = performance.now() + 250; rd = null; }, true);
    // appui long sur écran tactile = clic droit (pas pendant la marche / 360° : le doigt sert à regarder)
    let lp = null;
    document.addEventListener('touchstart', e => { if (e.touches.length !== 1) return; const t = e.touches[0];
      if (e.target.closest('#c') && (V.tool === 'walk' || V.tool === 'pano' || V.mode === 'ar')) return;
      clearTimeout(lp?.t); lp = {x:t.clientX, y:t.clientY, el:e.target, t:setTimeout(() => { lp.fired = true; this.onCtx({clientX:lp.x, clientY:lp.y, target:lp.el, preventDefault(){}, touch:true}); }, 560)}; }, {passive:true});
    document.addEventListener('touchmove', e => { if (lp && Math.hypot(e.touches[0].clientX - lp.x, e.touches[0].clientY - lp.y) > 10) clearTimeout(lp.t); }, {passive:true});
    document.addEventListener('touchend', e => { if (!lp) return; clearTimeout(lp.t); if (lp.fired && e.cancelable) e.preventDefault(); lp = null; });
  },
  native(t){ return !!t.closest?.('input:not([type=range]):not([type=checkbox]):not([type=radio]):not([type=button]):not([type=file]),textarea,[contenteditable="true"],.ctx-native'); },
  onCtx(e){
    const t = e.target; if (!t || !t.closest) return;
    if (t.closest('.ctx')){ e.preventDefault(); return; }
    if (this.native(t)) return;
    e.preventDefault?.();
    if (performance.now() < (this.skipUntil || 0)) return;
    let items = null;
    for (const z of this.zones){ const el = t.closest(z.sel); if (!el) continue; try { items = z.fn(e, el); } catch(err){ console.error('[ARCHIVISION] menu', err); } if (items) break; }
    if (!items) items = this.generic();
    if (!items.length) return;
    this.open(e.clientX, e.clientY, items);
  },
  /* menu de secours (partout ailleurs) */
  generic(){
    const s = String(window.getSelection?.() || '').trim(), out = [];
    if (s) out.push({t:'Copier', s:'Ctrl C', run:() => navigator.clipboard?.writeText(s).then(() => toast('Texte copié'))}, '-');
    if (spectreOK()) out.push({t:'Chercher une commande…', s:'Ctrl K', run:() => openSpectre()}, '-');
    out.push({t:'Recharger la page', s:'F5', run:() => location.reload()},
      {t:document.fullscreenElement ? 'Quitter le plein écran' : 'Plein écran', s:'F11', run:() => document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen?.().catch(() => toast('Plein écran refusé par le navigateur.'))},
      {t:'Copier le lien de la page', run:() => navigator.clipboard?.writeText(location.href.split('#')[0]).then(() => toast('Lien copié'))});
    return out;
  },
  open(x, y, items){ this.close(); this.prevFocus = document.activeElement; this.panel(items, x, y, 0); },
  panel(items, x, y, depth, from){
    if (!depth) this.t0 = performance.now();
    while (this.stack.length > depth) this.stack.pop().el.remove();
    const el = document.createElement('div'); el.className = 'ctx'; el.setAttribute('role', 'menu'); el.tabIndex = -1;
    const list = items.filter(Boolean).filter((it, i, a) => it !== '-' || (i > 0 && a[i - 1] !== '-' && i < a.length - 1));
    el.innerHTML = list.map((it, i) => it === '-' ? '<hr>' : it.h ? `<div class="ctx-h">${esc(it.h)}</div>`
      : `<button type="button" role="menuitem${it.on != null ? 'checkbox' : ''}" data-ci="${i}" class="${it.danger ? 'danger' : ''}${it.sub ? ' has' : ''}"${it.off ? ' disabled aria-disabled="true"' : ''}${it.on != null ? ` aria-checked="${!!it.on}"` : ''}>`
        + `<i class="ctx-c">${it.on ? '✓' : ''}</i><span>${esc(it.t)}</span>${it.s ? `<kbd>${esc(it.s)}</kbd>` : ''}${it.sub ? '<b class="ctx-ar">›</b>' : ''}</button>`).join('');
    document.body.appendChild(el);
    const W = el.offsetWidth, H = el.offsetHeight; let L = x, T = y;
    if (from){ const r = from.getBoundingClientRect(); L = r.right - 4; T = r.top - 6; if (L + W > innerWidth - 8) L = Math.max(8, r.left - W + 4); }
    else if (L + W > innerWidth - 8) L = Math.max(8, x - W);
    if (T + H > innerHeight - 8) T = Math.max(8, innerHeight - H - 8);
    el.style.left = Math.round(L) + 'px'; el.style.top = Math.round(T) + 'px';
    const entry = {el, items:list, depth}; this.stack.push(entry);
    el.addEventListener('click', e => { const b = e.target.closest('[data-ci]'); if (b && !b.disabled) this.choose(entry, +b.dataset.ci, b); });
    el.addEventListener('pointerover', e => { const b = e.target.closest('[data-ci]'); if (!b || e.pointerType === 'touch' || performance.now() - this.t0 < 120) return; b.focus({preventScroll:true}); clearTimeout(this.ht);
      const it = list[+b.dataset.ci]; this.ht = setTimeout(() => { if (it.sub && !b.disabled) this.panel(it.sub, 0, 0, depth + 1, b); else while (this.stack.length > depth + 1) this.stack.pop().el.remove(); }, it.sub ? 120 : 220); });
    requestAnimationFrame(() => (from ? el.querySelector('[data-ci]:not([disabled])') : el)?.focus({preventScroll:true}));
    return entry;
  },
  choose(entry, i, b){
    const it = entry.items[i]; if (!it || it.off) return;
    if (it.sub){ this.panel(it.sub, 0, 0, entry.depth + 1, b).el.querySelector('[data-ci]:not([disabled])')?.focus(); return; }
    this.close(true);
    try { const r = it.run?.(); if (r?.catch) r.catch(err => { console.error(err); toast(err?.message || 'Action impossible.'); }); } catch(err){ console.error(err); toast(err?.message || 'Action impossible.'); }
  },
  close(chosen){
    if (!this.stack.length) return; clearTimeout(this.ht);
    while (this.stack.length) this.stack.pop().el.remove();
    if (!chosen) try { this.prevFocus?.focus?.({preventScroll:true}); } catch(e){}
  },
  key(e){
    if (!this.stack.length) return;
    const top = this.stack[this.stack.length - 1], bs = [...top.el.querySelectorAll('[data-ci]:not([disabled])')], i = bs.indexOf(document.activeElement);
    const stop = () => { e.preventDefault(); e.stopImmediatePropagation(); };
    if (e.key === 'Escape'){ stop(); if (this.stack.length > 1){ this.stack.pop().el.remove(); this.stack[this.stack.length - 1].el.querySelector('[data-ci]:focus,[data-ci]')?.focus(); } else this.close(); }
    else if (e.key === 'ArrowDown' || e.key === 'ArrowUp'){ stop(); if (bs.length) bs[e.key === 'ArrowDown' ? (i + 1) % bs.length : (i - 1 + bs.length) % bs.length].focus(); }
    else if (e.key === 'Home' || e.key === 'End'){ stop(); bs[e.key === 'Home' ? 0 : bs.length - 1]?.focus(); }
    else if (e.key === 'ArrowRight'){ stop(); const b = document.activeElement; if (b?.classList.contains('has')) this.choose(top, +b.dataset.ci, b); }
    else if (e.key === 'ArrowLeft'){ stop(); if (this.stack.length > 1){ this.stack.pop().el.remove(); this.stack[this.stack.length - 1].el.querySelector('[data-ci].has:focus,[data-ci]')?.focus(); } }
    else if (e.key === 'Enter' || e.key === ' '){ stop(); const b = document.activeElement; if (b?.dataset.ci != null && !b.disabled) this.choose(top, +b.dataset.ci, b); }
    else if (e.key === 'Tab') stop();
  },
};
window.AVCtx = CTX;

/* ---------- Entrées communes (menus et Spectre) ---------- */
const clk = sel => () => { $(sel)?.click(); };
const TOOLS = [['orbit', 'Orbite', 'O'], ['walk', 'Marche', 'W'], ['pano', 'Immersion 360°', 'I'], ['inspect', 'Inspecter', 'S'], ['measure', 'Mesure', 'M'], ['section', 'Coupe', 'X']];
const STYLES_ = [['mat', 'Matériaux'], ['shaded', 'Ombré'], ['wire', 'Filaire'], ['xray', 'Rayons X']];
const VIEWS = [['iso', 'Vue 3D'], ['top', 'Vue de dessus'], ['front', 'Vue de face'], ['fit', 'Tout voir']];
const OPTS = [['shadows', 'Ombres portées'], ['edges', 'Arêtes des éléments'], ['ground', 'Sol et grille']];
const toolItems = () => TOOLS.map(([k, n, s]) => ({t:n, s, on:V.tool === k, off:!V.root, run:() => setTool(k)}));
const styleItems = () => STYLES_.map(([k, n]) => ({t:n, on:V.style === k, run:clk(`#styleGrp [data-style="${k}"]`)}));
const viewItems = () => VIEWS.map(([k, n]) => ({t:n, off:!V.root, run:clk(`.cams [data-cam="${k}"]`)}));
const optItems = () => OPTS.map(([k, n]) => ({t:n, on:!!V.opts[k], run:clk(`.status [data-opt="${k}"]`)}));
const themeItem = () => ({t:'Thème clair / sombre', run:clk('#userMenu [data-um="theme"]')});
const fileItems = () => [
  {t:'Mes maquettes', run:() => window.AVUI?.showHome()}, {t:'Importer une maquette…', run:clk('#fileMenu [data-act="import"]')}, {t:'Maquette d\'exemple', run:clk('#fileMenu [data-act="sample"]')}, '-',
  {t:'Exporter l\'image HD (.png)', off:!V.root, run:clk('#fileMenu [data-act="png"]')}, {t:'Exporter la maquette (.glb)', off:!V.root, run:clk('#fileMenu [data-act="glb"]')}, {t:'Exporter pour iPhone (.usdz)', off:!V.root, run:clk('#fileMenu [data-act="usdz"]')}];
const accountItems = () => [
  {t:'Ma licence', run:clk('#userMenu [data-um="licence"]')}, {t:'Code PIN et sécurité', run:clk('#userMenu [data-um="security"]')}, {t:'Réglages', run:clk('#userMenu [data-um="settings"]')}, themeItem(), '-',
  {t:'Verrouiller', run:clk('#userMenu [data-um="lock"]')}, {t:'Se déconnecter', run:clk('#userMenu [data-um="logout"]')}];
const selItems = o => [
  {t:'Voir les données', run:() => { select(o); openPanelTab('props'); }}, {t:'Centrer la vue', run:() => { select(o); focusObject(o); }},
  {t:'Isoler', run:() => { isolate(o); select(o); refreshPanel(); }}, {t:o.visible ? 'Masquer' : 'Afficher', run:() => { setVisible(o, !o.visible); refreshPanel(); }},
  {t:'Copier le nom', run:() => navigator.clipboard?.writeText(objLabel(o)).then(() => toast('Nom copié'))}];
const sceneItems = () => [
  {t:'Outils', sub:toolItems()}, {t:'Vues', sub:viewItems()}, {t:'Style d\'affichage', sub:styleItems()}, {t:'Affichage', sub:optItems()}, '-',
  {t:'Tout afficher', run:() => { showAll(); refreshPanel(); }}, {t:'Recentrer', s:'', run:clk('.cams [data-cam="fit"]')}, {t:'Effacer les mesures', off:!measures.length, run:() => clearMeasures()},
  {t:'Retirer la coupe', off:!renderer.clippingPlanes.length, run:() => setSection(null)}, '-',
  {t:'Capture', s:'C', run:() => doCapture()}, {t:'Voir en réalité augmentée', run:clk('#arGo')}, '-', {t:'Fichier', sub:fileItems()}, {t:'Chercher une commande…', s:'Ctrl K', run:() => openSpectre()}];

/* ---------- Zones ---------- */
CTX.zone('#arUi, #arPc, #splash', () => []);                     // pas de menu en réalité augmentée ni pendant l'intro
CTX.zone('#c', e => {
  if (!V.root) return null;
  const hit = pickAt(e.clientX, e.clientY), items = [];
  if (hit){ const o = elementOf(hit.object); items.push({h:objLabel(o)}, ...selItems(o)); if (V.tool === 'walk' || V.tool === 'pano') items.push({t:'Me déplacer ici', run:() => moveTo(hit.point, hit.face)}); items.push('-'); }
  return [...items, ...sceneItems()];
});
CTX.zone('.tr[data-tk]', (e, row) => { const o = byKey(row.dataset.tk); return o ? [{h:objLabel(o)}, ...selItems(o), '-', {t:'Tout afficher', run:() => { showAll(); refreshPanel(); }}] : null; });
CTX.zone('.pthumb[data-photo]', (e, b) => [{t:'Ouvrir la photo', run:() => b.click()}]);
CTX.zone('#home [data-photo]', (e, b) => [{t:'Ouvrir la photo', run:() => b.click()}]);
CTX.zone('#home [data-open]', (e, card) => { const it = window.AVUI?.modelItems?.(card.dataset.open); return it ? [...it, '-', {t:'Importer une maquette…', run:clk('#homeNewP')}] : null; });
CTX.zone('.tools', () => [{h:'Outils de visite'}, ...toolItems(), '-', {t:'Vues', sub:viewItems()}]);
CTX.zone('.vtools', () => [{h:'Affichage'}, ...styleItems(), '-', ...viewItems(), '-', ...optItems()]);
CTX.zone('.status', () => [{h:'Affichage'}, ...optItems()]);
CTX.zone('.tabs', () => [{h:'Panneau'}, ...[['tree', 'Maquette'], ['props', 'Données'], ['gallery', 'Galerie'], ['info', 'Infos']].map(([k, n]) => ({t:n, on:activeTab === k, run:() => openPanelTab(k)}))]);
CTX.zone('#panel', () => [{t:'Maquette', run:() => openPanelTab('tree')}, {t:'Données', run:() => openPanelTab('props')}, {t:'Galerie', run:() => openPanelTab('gallery')}, {t:'Infos', run:() => openPanelTab('info')}, '-', {t:'Tout afficher', run:() => { showAll(); refreshPanel(); }}]);
CTX.zone('#home .h-top, #home .h-hero, #home .h-body, #home .h-scroll', () => {
  const m = window.AVUI?.where?.() === 'home';
  return m ? [{t:'Importer une maquette…', run:clk('#homeNewP')}, {t:'Maquette d\'exemple', run:clk('#homeSample')}, {t:'Voir en réalité augmentée', run:clk('#homeAR')}, {t:'Voir en AR sur le PC', run:clk('#homeARpc')}, '-',
    {t:'Maquettes', run:clk('#homeTabs [data-htab="mine"]')}, {t:'Galerie', run:clk('#homeTabs [data-htab="gallery"]')}, {t:'Actualiser', run:() => window.AVUI.refreshHome()}, {t:'Synchroniser maintenant', run:() => { window.AVSync?.start(); window.AVSync?.now(); }}, '-',
    themeItem(), {t:'Chercher une commande…', s:'Ctrl K', run:() => openSpectre()}, {t:'Mon compte', sub:accountItems()}] : null;
});
CTX.zone('#app .top, #app .cmd', () => [{t:'Mes maquettes', run:() => window.AVUI?.showHome()}, {t:'Fichier', sub:fileItems()}, {t:'Capture', s:'C', off:!V.root, run:() => doCapture()}, {t:'Voir en réalité augmentée', run:clk('#arGo')}, '-', themeItem(), {t:'Mon compte', sub:accountItems()}, {t:'Chercher une commande…', s:'Ctrl K', run:() => openSpectre()}]);
CTX.zone('#app', () => V.root ? sceneItems() : [{t:'Mes maquettes', run:() => window.AVUI?.showHome()}, {t:'Maquette d\'exemple', run:clk('#fileMenu [data-act="sample"]')}, '-', themeItem()]);
CTX.init();

/* ---------- Spectre : palette de commandes (Ctrl K) ---------- */
const SP = {all:[], items:[], i:0};
const spectreOK = () => ['home', 'app'].includes(window.AVUI?.where?.()) && !window.AVUI?.dlgOpen?.();
const norm = s => String(s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
function spectreActions(){
  const A = [], add = (g, t, run, k = '', s = '', off = false) => { if (!off) A.push({g, t, run, k, s}); };
  const home = window.AVUI?.where?.() === 'home', hasM = !!V.root && !home;
  TOOLS.forEach(([k, n, s]) => add('Visite', n, () => setTool(k), 'outil mode ' + k, s, !hasM));
  VIEWS.forEach(([k, n]) => add('Vue', n, clk(`.cams [data-cam="${k}"]`), 'camera recentrer', '', !hasM));
  STYLES_.forEach(([k, n]) => add('Style', n, clk(`#styleGrp [data-style="${k}"]`), 'affichage rendu', '', !hasM));
  OPTS.forEach(([k, n]) => add('Affichage', (V.opts[k] ? 'Masquer : ' : 'Afficher : ') + n.toLowerCase(), clk(`.status [data-opt="${k}"]`), 'ombre arete sol grille', '', !hasM));
  add('Visite', 'Capture de la vue', () => doCapture(), 'photo image', 'C', !hasM);
  add('Visite', 'Voir en réalité augmentée', home ? clk('#homeAR') : clk('#arGo'), 'ar realite augmentee poser');
  add('Visite', 'Tout afficher', () => { showAll(); refreshPanel(); }, 'masque visibilite', '', !hasM);
  add('Visite', 'Effacer les mesures', () => clearMeasures(), 'mesure', '', !hasM || !measures.length);
  add('Visite', 'Retirer la coupe', () => setSection(null), 'plan coupe', '', !hasM || !renderer.clippingPlanes.length);
  if (hasM && V.sel){ const o = V.sel; add('Sélection', `Données : ${objLabel(o)}`, () => openPanelTab('props'), 'bim ifc'); add('Sélection', `Centrer sur ${objLabel(o)}`, () => focusObject(o)); add('Sélection', `Isoler ${objLabel(o)}`, () => { isolate(o); refreshPanel(); }); add('Sélection', `Masquer ${objLabel(o)}`, () => { setVisible(o, false); refreshPanel(); }); }
  [['tree', 'Maquette', 'hierarchie arborescence'], ['props', 'Données', 'bim ifc proprietes'], ['gallery', 'Galerie', 'photos captures'], ['info', 'Infos', 'unites']].forEach(([k, n, kw]) => add('Panneau', 'Panneau : ' + n, () => openPanelTab(k), kw, '', !hasM));
  add('Fichier', 'Mes maquettes', () => window.AVUI?.showHome(), 'accueil');
  add('Fichier', 'Importer une maquette', clk('#fileMenu [data-act="import"]'), 'glb ifc gltf zip fichier');
  add('Fichier', 'Ouvrir la maquette d\'exemple', clk('#fileMenu [data-act="sample"]'), 'maison demo');
  add('Export', 'Exporter l\'image HD (.png)', clk('#fileMenu [data-act="png"]'), 'capture', '', !hasM); add('Export', 'Exporter la maquette (.glb)', clk('#fileMenu [data-act="glb"]'), '', '', !hasM); add('Export', 'Exporter pour iPhone (.usdz)', clk('#fileMenu [data-act="usdz"]'), 'quick look apple', '', !hasM);
  add('Fichier', 'Voir en AR sur le PC', home ? clk('#homeARpc') : clk('#arPcBtn'), 'code session direct');
  add('Fichier', 'Synchroniser les maquettes maintenant', () => { window.AVSync?.start(); window.AVSync?.now(); toast('Synchronisation lancée'); }, 'cloud appareils');
  (window.AVUI?.models?.() || []).slice(0, 30).forEach(m => { add('Maquettes', `Ouvrir « ${m.name} »`, () => window.AVUI.openModel(m.id), 'visiter'); add('Maquettes', `« ${m.name} » en réalité augmentée`, () => window.AVUI.openModel(m.id, {ar:true}), 'ar'); });
  add('Compte', 'Ma licence', clk('#userMenu [data-um="licence"]'), 'cle activer'); add('Compte', 'Fiche client', clk('#userMenu [data-um="fiche"]')); add('Compte', 'Code PIN et sécurité', clk('#userMenu [data-um="security"]'), 'empreinte biometrie');
  add('Compte', 'Thème clair / sombre', clk('#userMenu [data-um="theme"]'), 'apparence nuit'); add('Compte', 'Réglages', clk('#userMenu [data-um="settings"]'), 'taille molette qualite');
  add('Compte', 'Verrouiller', clk('#userMenu [data-um="lock"]'), 'pin'); add('Compte', 'Se déconnecter', clk('#userMenu [data-um="logout"]'), 'quitter deconnexion');
  return A;
}
function openSpectre(){
  if (!spectreOK()) return; CTX.close(); const sp = $('#spectre'); if (!sp) return;
  SP.prev = document.activeElement; sp.hidden = false; const q = $('#spQ'); q.value = ''; SP.all = spectreActions(); SP.i = 0; drawSpectre(); setTimeout(() => q.focus(), 10);
}
function closeSpectre(){ const sp = $('#spectre'); if (!sp || sp.hidden) return; sp.hidden = true; try { SP.prev?.focus?.({preventScroll:true}); } catch(e){} }
function drawSpectre(){
  const words = norm($('#spQ').value.trim()).split(/\s+/).filter(Boolean);
  const SP_START = ['Mes maquettes', 'Importer une maquette', 'Marche', 'Orbite', 'Capture de la vue', 'Voir en réalité augmentée', 'Thème clair / sombre', 'Ouvrir la maquette d\'exemple'];
  let l;
  if (words.length) l = SP.all.map(a => { const hay = norm(a.t + ' ' + a.g + ' ' + a.k); if (!words.every(w => hay.includes(w))) return null; const tt = norm(a.t); return {...a, sc:tt.startsWith(words[0]) ? 0 : tt.includes(words[0]) ? 1 : 2}; }).filter(Boolean).sort((a, b) => a.sc - b.sc);
  else l = SP_START.map(t => SP.all.find(a => a.t === t)).filter(Boolean);
  SP.items = l.slice(0, 50); SP.i = clamp(SP.i, 0, Math.max(0, SP.items.length - 1));
  $('#spList').innerHTML = (words.length ? '' : '<p class="sp-h">Suggestions <small>Tapez « marche », « filaire », « exporter », « thème »…</small></p>')
    + (SP.items.length ? SP.items.map((a, i) => `<button type="button" role="option" data-sp="${i}" class="${i === SP.i ? 'on' : ''}"><span>${esc(a.t)}</span>${a.s ? `<kbd>${esc(a.s)}</kbd>` : ''}<small>${esc(a.g)}</small></button>`).join('') : '<p class="sp-none">Rien ne correspond. Essayez « outil », « export », « vue » ou « thème ».</p>');
  $('#spList .on')?.scrollIntoView({block:'nearest'});
}
function runSpectre(i){ const a = SP.items[i]; if (!a) return; closeSpectre(); setTimeout(() => { try { const r = a.run(); r?.catch?.(err => toast(err?.message || 'Action impossible.')); } catch(err){ console.error(err); toast(err?.message || 'Action impossible.'); } }, 10); }
$('#spQ').addEventListener('input', () => { SP.i = 0; drawSpectre(); });
$('#spQ').addEventListener('keydown', e => {
  e.stopPropagation();
  if (e.key === 'ArrowDown'){ e.preventDefault(); SP.i = Math.min(SP.items.length - 1, SP.i + 1); drawSpectre(); }
  else if (e.key === 'ArrowUp'){ e.preventDefault(); SP.i = Math.max(0, SP.i - 1); drawSpectre(); }
  else if (e.key === 'Enter'){ e.preventDefault(); runSpectre(SP.i); }
  else if (e.key === 'Escape'){ e.preventDefault(); closeSpectre(); }
});
$('#spList').addEventListener('click', e => { const b = e.target.closest('[data-sp]'); if (b) runSpectre(+b.dataset.sp); });
$('#spectre').addEventListener('mousedown', e => { if (e.target.id === 'spectre') closeSpectre(); });
window.addEventListener('keydown', e => {
  if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'k'){
    if (!spectreOK() && $('#spectre').hidden) return;      // pas sur l'écran de compte
    e.preventDefault(); e.stopImmediatePropagation(); $('#spectre').hidden ? openSpectre() : closeSpectre();
  }
}, true);
window.AVSpectre = {open:openSpectre, close:closeSpectre};
if (/Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent)) document.querySelectorAll('.sp-kbd').forEach(k => { k.textContent = '⌘ K'; });
document.querySelectorAll('[data-spectre]').forEach(b => b.addEventListener('click', openSpectre));
