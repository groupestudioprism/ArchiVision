/* ============ BIM : hiérarchie de la maquette, données des éléments (ARCHIPLAN, IFC, glTF) ============ */
const KIND_FR = {wall:'Mur', slab:'Dalle', roof:'Toiture', col:'Poteau', beam:'Poutre', stair:'Escalier', inst:'Composant', zone:'Volume', terrain:'Terrain', socle:'Socle', environnement:'Environnement'};
function objLabel(o){
  const ap = o.userData?.archiplan, ifc = o.userData?.ifc;
  if (ifc) return ifc.nom || IFC_FR[String(ifc.classe).toUpperCase()] || ifc.classe || o.name;
  if (o === V.root && V.rec?.name) return V.rec.name;
  if (ap?.famille) return `${ap.famille}${ap.id ? ' ' + ap.id : ''}`;
  if (ap?.type && KIND_FR[ap.type] && ap.id) return `${/cloison/i.test(o.name) ? 'Cloison' : KIND_FR[ap.type]} ${ap.id}`;
  return o.name || (o.isMesh ? 'Maillage' : 'Groupe');
}
function objKind(o){
  const ap = o.userData?.archiplan, ifc = o.userData?.ifc;
  if (ifc) return IFC_FR[String(ifc.classe).toUpperCase()] || ifc.classe; if (ap?.type) return KIND_FR[ap.type] || ap.type; if (o.userData?.niveau) return 'Niveau';
  if (o.isMesh) return 'Maillage'; return '';
}
/* enfants « utiles » : on saute les groupes intermédiaires sans nom ni données (fréquents dans les GLB) */
function kids(o){
  const out = [];
  for (const c of o.children){
    if (c.userData?.__helper || c.isLight || c.isCamera) continue;
    if (!c.isMesh && !hasInfo(c) && !c.userData?.niveau && (!c.name || /^(node|group|object|scene|root)?_?\d*$/i.test(c.name)) && c.children.length) out.push(...kids(c));
    else out.push(c);
  }
  return out;
}
const isLeafEl = o => hasInfo(o) && !kids(o).some(c => !c.isMesh);
function countEls(o){ let n = 0; o.traverse(x => { if (x !== o && (x.userData?.archiplan?.type || x.userData?.ifc) && !x.userData?.ifc?.classe?.match(/^Ifc(Project|Site|Building|BuildingStorey)$/)) n++; }); return n; }

const treeOpen = new Set(); let treeQ = '';
const ICO = {
  root:'<svg viewBox="0 0 24 24"><path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M4 7.5l8 4.5 8-4.5M12 12v9" opacity=".6"/></svg>',
  lvl:'<svg viewBox="0 0 24 24"><path d="M3 9l9-5 9 5-9 5z"/><path d="M3 14l9 5 9-5" opacity=".6"/></svg>',
  el:'<svg viewBox="0 0 24 24"><rect x="5" y="5" width="14" height="14" rx="2"/></svg>',
  mesh:'<svg viewBox="0 0 24 24"><path d="M5 19L12 5l7 14z"/></svg>',
  grp:'<svg viewBox="0 0 24 24"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg>',
  eye:'<svg viewBox="0 0 24 24"><path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12z"/><circle cx="12" cy="12" r="2.5"/></svg>',
  eyeOff:'<svg viewBox="0 0 24 24"><path d="M3 3l18 18M10.6 6.1A10 10 0 0 1 12 6c6.5 0 10 6 10 6a17 17 0 0 1-3.2 3.8M6.6 6.6C3.9 8.3 2 12 2 12s3.5 6 10 6a9.6 9.6 0 0 0 4.4-1"/></svg>',
  dots:'<svg viewBox="0 0 24 24" fill="currentColor" stroke="none"><circle cx="12" cy="5" r="1.7"/><circle cx="12" cy="12" r="1.7"/><circle cx="12" cy="19" r="1.7"/></svg>',
};
const keyOf = o => o.uuid, byKey = k => V.root?.getObjectByProperty('uuid', k);
function icoFor(o, depth){ if (depth === 0) return ICO.root; if (o.userData?.niveau || o.userData?.ifc?.classe === 'IfcBuildingStorey') return ICO.lvl; if (o.isMesh) return ICO.mesh; if (hasInfo(o)) return ICO.el; return ICO.grp; }
function treeRows(o, depth, q, out, budget){
  if (out.length > budget.max) return;
  const ks = kids(o), open = treeOpen.has(keyOf(o)) || (q && depth < 6);
  const label = objLabel(o), meta = depth === 0 ? `${countEls(o) || V.meshes.length} él.` : objKind(o);
  const matchSelf = !q || label.toLowerCase().includes(q) || meta.toLowerCase().includes(q);
  let sub = [];
  if (ks.length && (open || q)) for (const c of ks) treeRows(c, depth + 1, q, sub, budget);
  if (q && !matchSelf && !sub.length) return;
  const twist = ks.length && !(o.isMesh && !ks.some(c => !c.isMesh)) ? (open || q ? 'true' : 'false') : null;
  out.push(`<div class="tr${V.sel === o ? ' on' : ''}${o.visible ? '' : ' off'}" style="--d:${depth}" data-tk="${keyOf(o)}">
    <button class="tw" data-twist="${keyOf(o)}" ${twist === null ? 'disabled aria-hidden="true"' : `aria-expanded="${twist}" aria-label="${twist === 'true' ? 'Replier' : 'Déplier'}"`}>${twist === null ? '' : '<svg viewBox="0 0 24 24"><path d="M9 6l6 6-6 6"/></svg>'}</button>
    <span class="ti">${icoFor(o, depth)}</span><span class="tl">${depth === 0 ? '<b>' + esc(label) + '</b>' : esc(label)}</span>${meta ? `<span class="tm">${esc(meta)}</span>` : ''}
    ${depth ? `<button class="te" data-teye="${keyOf(o)}" title="${o.visible ? 'Masquer' : 'Afficher'}" aria-label="${o.visible ? 'Masquer' : 'Afficher'}">${o.visible ? ICO.eye : ICO.eyeOff}</button>` : ''}
    <button class="td" data-tmenu="${keyOf(o)}" aria-label="Actions">${ICO.dots}</button></div>`);
  if (twist === 'true' || (q && sub.length)) out.push(...sub);
  budget.n++;
}
function renderTree(){
  if (!V.root) return pb().innerHTML = `<div class="hint">Aucun élément à afficher. Placez ou ouvrez d'abord une maquette.</div>`;
  if (!treeOpen.size){
    treeOpen.add(keyOf(V.root));
    const SPATIAL = /^Ifc(Project|Site|Building|BuildingStorey)$/;
    const walk = (o, d) => { for (const c of kids(o)){ if (d === 0 && kids(c).length < 40 || SPATIAL.test(c.userData?.ifc?.classe || '') && kids(c).length < (c.userData.ifc.classe === 'IfcBuildingStorey' ? 30 : 80) || c.userData?.archiplan?.version && d < 2){ treeOpen.add(keyOf(c)); walk(c, d + 1); } } };
    walk(V.root, 0);
  }
  const out = [], budget = {n:0, max:1500}; treeRows(V.root, 0, treeQ.trim().toLowerCase(), out, budget);
  const single = V.meshes.length <= 1;
  pb().innerHTML = `<div class="ph"><h2>${esc(V.rec?.name || 'Maquette')}</h2><span class="tag">${esc(({ifc:'IFC', glb:'GLB', gltf:'glTF', sample:'Exemple'})[V.rec?.kind] || 'GLB')}</span></div>
    ${single ? '<p class="hint">Cette maquette ne contient qu\'un seul élément (maillage fusionné, ou bloc provisoire d\'un IFC non converti).</p>' : ''}
    <div class="tree-h"><input class="libsearch tq" id="treeQ" type="search" placeholder="Filtrer : mur, porte, IfcSlab…" value="${esc(treeQ)}" aria-label="Filtrer les éléments"><button class="ib ghost" data-tact="collapse" title="Tout replier">−</button><button class="ib ghost" data-tact="expand" title="Déplier un niveau">+</button></div>
    <div class="tree" role="tree">${out.join('')}${out.length > budget.max ? '<p class="hint">Liste tronquée : filtrez pour retrouver un élément.</p>' : ''}</div>
    <div class="btns"><button class="btn" data-tact="showall">Tout afficher</button><button class="btn" data-tact="fit">Recentrer</button></div>`;
  const q = $('#treeQ'); q.oninput = () => { treeQ = q.value; const pos = q.selectionStart; renderTree(); const nq = $('#treeQ'); nq.focus(); nq.setSelectionRange(pos, pos); };
}
function treeMenu(btn, o){
  const m = $('#treeMenu'), items = [
    ['focus', 'Centrer la vue', 'Zoom sur l\'élément'], ['isolate', 'Isoler', 'Masquer tout le reste'], ['toggle', o.visible ? 'Masquer' : 'Afficher', ''],
    ['props', 'Voir les données', 'Onglet Données'], ['showall', 'Tout afficher', '']];
  m.innerHTML = items.map(([a, b, s]) => `<button data-m="${a}"><b>${b}</b>${s ? `<span>${s}</span>` : ''}</button>`).join('');
  m.hidden = false; const r = btn.getBoundingClientRect();
  m.style.top = Math.min(r.bottom + 4, innerHeight - m.offsetHeight - 10) + 'px'; m.style.left = Math.max(10, r.right - m.offsetWidth) + 'px';
  m.onclick = e => { const a = e.target.closest('[data-m]')?.dataset.m; if (!a) return; m.hidden = true;
    if (a === 'focus'){ select(o, {from:'tree'}); focusObject(o); } else if (a === 'isolate'){ isolate(o); select(o, {from:'tree'}); } else if (a === 'toggle') setVisible(o, !o.visible);
    else if (a === 'props'){ select(o, {from:'tree'}); setTab('props'); } else if (a === 'showall') showAll();
    refreshPanel(); };
}
document.addEventListener('click', e => { const m = $('#treeMenu'); if (!m.hidden && !m.contains(e.target) && !e.target.closest('[data-tmenu]')) m.hidden = true; });

/* ---------- Données de l'élément sélectionné ---------- */
const AP_LABEL = {id:'Identifiant', type:'Type', composition:'Composition', U:'U (W/m²·K)', Uw:'Uw (W/m²·K)', Ud:'Ud (W/m²·K)', lot:'Lot', famille:'Famille', categorie:'Catégorie', typeFamille:'Type de famille', materiau:'Matériau', hote:'Hôte', longueur:'Longueur (m)', pente:'Pente', source:'Source', niveau:'Niveau', altitude:'Altitude (m)', objet:'Objet'};
const IFC_LABEL = {classe:'Classe IFC', nom:'Nom', nomLong:'Nom long', description:'Description', typeObjet:'Type d\'objet', type:'Type', typePredefini:'Type prédéfini', reference:'Référence', globalId:'GlobalId', expressID:'Identifiant STEP', altitude:'Altitude (m)'};
const PSET_LABEL = {FireRating:'Résistance au feu', IsExternal:'Extérieur', LoadBearing:'Porteur', ThermalTransmittance:'U (W/m²·K)', AcousticRating:'Indice acoustique', Reference:'Référence', Status:'Statut', Width:'Largeur', Height:'Hauteur', Length:'Longueur', Depth:'Profondeur', Thickness:'Épaisseur', Area:'Surface', NetArea:'Surface nette', GrossArea:'Surface brute', Volume:'Volume', NetVolume:'Volume net', GrossVolume:'Volume brut', NetSideArea:'Surface latérale nette', GrossSideArea:'Surface latérale brute', Perimeter:'Périmètre', Combustible:'Combustible', SurfaceSpreadOfFlame:'Propagation de flamme', HandicapAccessible:'Accessible PMR', FireExit:'Sortie de secours', Compartmentation:'Compartimentage'};
const fmtVal = v => v === true || v === 'T' || v === '.T.' ? 'Oui' : v === false || v === 'F' || v === '.F.' ? 'Non' : typeof v === 'number' ? (Number.isInteger(v) ? String(v) : nf(v, Math.abs(v) < 10 ? 3 : 2).replace(/0+$/, '').replace(/,$/, '')) : String(v);
const kvRows = rows => rows.filter(([, v]) => v !== undefined && v !== null && v !== '').map(([k, v]) => `<dt>${esc(k)}</dt><dd>${esc(fmtVal(v))}</dd>`).join('');
function layersHtml(couches){
  if (!Array.isArray(couches) || !couches.length) return '';
  const tot = couches.reduce((s, c) => s + (+c.epaisseur || 0), 0);
  return `<div class="grp"><h3>Couches (extérieur → intérieur)</h3><div class="layers">${couches.map(c => `<div class="ly"><i style="flex:${Math.max(0.04, (+c.epaisseur || 0) / (tot || 1))}"></i><span>${esc(c.materiau)}</span><b>${c.epaisseur ? nf(c.epaisseur * 100, 1) + ' cm' : '—'}</b></div>`).join('')}</div>${tot ? `<p class="hint">Épaisseur totale : ${nf(tot * 100, 1)} cm</p>` : ''}</div>`;
}
function geomRows(o){
  const b = new THREE.Box3().setFromObject(o); if (b.isEmpty()) return [];
  const s = b.getSize(new THREE.Vector3()); let n = 0, mats = new Set(); o.traverse(x => { if (x.isMesh && x.userData.__orig){ n++; [x.userData.__orig].flat().forEach(m => m?.name && !/^IFC [\d.,]+$/.test(m.name) && mats.add(m.name)); } });
  return {rows:[['Dimensions (L × l × h)', `${nf(s.x, 2)} × ${nf(s.z, 2)} × ${nf(s.y, 2)} m`], ['Base (altitude)', `${nf(b.min.y, 2)} m`], ['Sous-éléments', n]], mats:[...mats]};
}
function renderProps(){
  const o = V.sel;
  if (!o) return pb().innerHTML = `<div class="ph"><h2>Données de l'élément</h2></div><div class="empty-props">${ICO.el}<p>Touchez un élément de la maquette (outil <b>Inspecter</b>, ou en orbite) pour afficher ses données BIM : classe IFC, matériaux, composition, propriétés.</p></div>`;
  const ap = o.userData?.archiplan, ifc = o.userData?.ifc, g = geomRows(o);
  let h = `<div class="ph"><h2>${esc(objLabel(o))}</h2>${objKind(o) ? `<span class="tag">${esc(objKind(o))}</span>` : ''}</div>`;
  if (ifc){
    h += `<div class="grp"><h3>IFC Object Data</h3><dl class="kv">${kvRows(Object.entries(IFC_LABEL).map(([k, l]) => [l, ifc[k]]))}${ifc.materiaux ? kvRows([['Matériaux', ifc.materiaux.join(', ')]]) : ''}</dl></div>`;
    h += layersHtml(ifc.couches);
    for (const [ps, props] of Object.entries(ifc.psets || {})) h += `<details class="grp pset"${/Common|Quantit/i.test(ps) ? ' open' : ''}><summary>${esc(ps)}</summary><dl class="kv">${kvRows(Object.entries(props).map(([k, v]) => [PSET_LABEL[k] || k, v]))}</dl></details>`;
  }
  if (ap){
    const rows = Object.entries(ap).filter(([k, v]) => typeof v !== 'object' && k !== 'version').map(([k, v]) => [AP_LABEL[k] || k, k === 'type' ? (KIND_FR[v] || v) : v]);
    h += `<div class="grp"><h3>Données ARCHIPLAN</h3><dl class="kv">${kvRows(rows)}</dl></div>${layersHtml(ap.couches)}`;
    if (ap.parametres && typeof ap.parametres === 'object') h += `<details class="grp pset" open><summary>Paramètres de la famille</summary><dl class="kv">${kvRows(Object.entries(ap.parametres).filter(([, v]) => typeof v !== 'object').map(([k, v]) => [k, v]))}</dl></details>`;
  }
  const other = Object.entries(o.userData || {}).filter(([k, v]) => !k.startsWith('__') && !['archiplan', 'ifc', 'bim', 'name'].includes(k) && (typeof v !== 'object' || v === null));
  if (other.length) h += `<div class="grp"><h3>Propriétés (extras glTF)</h3><dl class="kv">${kvRows(other.map(([k, v]) => [AP_LABEL[k] || k, v]))}</dl></div>`;
  if (g.rows) h += `<div class="grp"><h3>Géométrie</h3><dl class="kv">${kvRows(g.rows)}${g.mats.length ? kvRows([['Matériaux 3D', g.mats.slice(0, 8).join(', ')]]) : ''}</dl></div>`;
  if (!ap && !ifc && !other.length) h += `<p class="hint">Cet objet ne porte pas de données BIM : le fichier ne contient que la géométrie. Exportez en IFC, ou en GLB depuis ARCHIPLAN, pour garder les informations.</p>`;
  h += `<div class="btns"><button class="btn" data-pact="focus">Centrer</button><button class="btn" data-pact="isolate">Isoler</button><button class="btn" data-pact="hide">Masquer</button><button class="btn" data-pact="showall">Tout afficher</button></div>`;
  pb().innerHTML = h;
}
/* fiche courte (réalité augmentée) */
function shortInfo(o){
  const ap = o.userData?.archiplan, ifc = o.userData?.ifc, rows = [];
  if (ifc){ rows.push(['Classe', ifc.classe], ['Type', ifc.type || ifc.typeObjet], ['Matériau', ifc.materiaux?.join(', ') || ifc.couches?.map(c => c.materiau).join(' / ')]);
    const fr = Object.values(ifc.psets || {}).map(p => p.FireRating).find(Boolean); if (fr) rows.push(['Résistance au feu', fr]); }
  if (ap) rows.push(['Type', KIND_FR[ap.type] || ap.type], ['Composition', ap.composition || ap.famille], ['U', ap.U ? `${fmtVal(ap.U)} W/m²·K` : null], ['Lot', ap.lot]);
  const g = geomRows(o); if (g.rows) rows.push(g.rows[0]);
  return `<b>${esc(objLabel(o))}</b><dl class="kv">${kvRows(rows)}</dl>`;
}
