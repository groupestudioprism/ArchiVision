/* ============ Chargement des maquettes : GLB, glTF (+ .bin, textures), IFC (web-ifc), ZIP d'ARCHIPLAN ============ */
const draco = new DRACOLoader(); draco.setDecoderPath(DRACO_URL);
function gltfLoader(manager){ const l = new GLTFLoader(manager); l.setDRACOLoader(draco); l.setMeshoptDecoder(MeshoptDecoder); return l; }
async function parseGLB(buf){
  const g = await new Promise((res, rej) => gltfLoader().parse(buf, '', res, e => rej(new Error('Le fichier reçu n\'est pas un GLB valide.' + (e?.message ? ' (' + e.message + ')' : '')))));
  const root = g.scene || g.scenes?.[0]; if (!root) throw new Error('Le GLB ne contient aucune scène.');
  if (g.animations?.length) root.userData.__anims = g.animations.length;
  return root;
}
async function parseGLTF(text, files){
  const urls = new Map(), made = [];
  for (const f of files){ const u = URL.createObjectURL(f); made.push(u); urls.set(f.name.toLowerCase(), u); }
  const mgr = new THREE.LoadingManager();
  mgr.setURLModifier(url => { if (/^(blob|data):/.test(url)) return url; const n = decodeURIComponent(url.split(/[\\/]/).pop().split('?')[0]).toLowerCase(); return urls.get(n) || url; });
  try {
    const g = await new Promise((res, rej) => gltfLoader(mgr).parse(text, '', res, e => rej(new Error('glTF illisible' + (e?.message ? ' : ' + e.message : '')))));
    return g.scene || g.scenes[0];
  } finally { setTimeout(() => made.forEach(u => URL.revokeObjectURL(u)), 20000); }
}

/* export GLB : les données BIM (userData) partent dans « extras » ; les objets internes à la vue sont retirés le temps de l'export */
async function exportGLB(root){
  const saved = [];
  root.traverse(o => {
    const ud = o.userData; if (!ud) return;
    const priv = Object.keys(ud).filter(k => k.startsWith('__'));
    if (priv.length){ saved.push([o, Object.fromEntries(priv.map(k => [k, ud[k]])), o.material]); priv.forEach(k => delete ud[k]); }
  });
  for (const [o, priv] of saved){ if (priv.__orig) o.material = priv.__orig; if (priv.__edges) o.remove(priv.__edges); }
  const vis = []; root.traverse(o => { if (!o.visible){ vis.push(o); o.visible = true; } });
  const pos = root.position.clone(), sc = root.scale.clone(); root.position.set(0, 0, 0); root.scale.setScalar(1);
  try {
    const out = await new GLTFExporter().parseAsync(root, {binary:true, onlyVisible:false, maxTextureSize:4096});
    return new Blob([out], {type:'model/gltf-binary'});
  } finally {
    root.position.copy(pos); root.scale.copy(sc); root.updateMatrixWorld(true);
    vis.forEach(o => o.visible = false);
    for (const [o, priv, mat] of saved){ Object.assign(o.userData, priv); o.material = mat; if (priv.__edges) o.add(priv.__edges); }
  }
}

/* ---------- IFC → Three.js (web-ifc, chargé à la demande) ---------- */
let ifcApiP = null;
function ifcApi(){
  return ifcApiP || (ifcApiP = (async () => {
    const W = await import(/* @vite-ignore */ WEB_IFC_URL + 'web-ifc-api.js');
    const api = new W.IfcAPI(); api.SetWasmPath(WEB_IFC_URL, true); await api.Init(); api.SetLogLevel?.(W.LogLevel?.LOG_LEVEL_OFF ?? 6);
    return {api, W};
  })().catch(e => { ifcApiP = null; throw new Error('Le lecteur IFC (web-ifc) n\'a pas pu se charger. Vérifiez la connexion.'); }));
}
const IFC_FR = {IFCPROJECT:'Projet', IFCSITE:'Site', IFCBUILDING:'Bâtiment', IFCBUILDINGSTOREY:'Niveau', IFCSPACE:'Espace', IFCWALL:'Mur', IFCWALLSTANDARDCASE:'Mur', IFCSLAB:'Dalle', IFCROOF:'Toiture',
  IFCDOOR:'Porte', IFCWINDOW:'Fenêtre', IFCCOLUMN:'Poteau', IFCBEAM:'Poutre', IFCSTAIR:'Escalier', IFCSTAIRFLIGHT:'Volée d\'escalier', IFCRAILING:'Garde-corps', IFCCOVERING:'Revêtement',
  IFCFURNISHINGELEMENT:'Mobilier', IFCFURNITURE:'Mobilier', IFCMEMBER:'Membrure', IFCPLATE:'Plaque', IFCCURTAINWALL:'Mur-rideau', IFCBUILDINGELEMENTPROXY:'Élément', IFCFOOTING:'Fondation', IFCPILE:'Pieu',
  IFCRAMP:'Rampe', IFCRAMPFLIGHT:'Volée de rampe', IFCOPENINGELEMENT:'Ouverture', IFCFLOWTERMINAL:'Terminal', IFCFLOWSEGMENT:'Réseau', IFCFLOWFITTING:'Raccord', IFCSANITARYTERMINAL:'Sanitaire',
  IFCDISTRIBUTIONELEMENT:'Équipement', IFCCHIMNEY:'Cheminée', IFCSHADINGDEVICE:'Occultation', IFCREINFORCINGBAR:'Armature', IFCELEMENTASSEMBLY:'Assemblage', IFCBUILDINGELEMENTPART:'Partie', IFCGEOGRAPHICELEMENT:'Élément géographique'};
const IFC_CC = {}; // IFCWALLSTANDARDCASE → IfcWallStandardCase (noms d'usage)
['Project', 'Site', 'Building', 'BuildingStorey', 'Space', 'Wall', 'WallStandardCase', 'Slab', 'Roof', 'Door', 'Window', 'Column', 'Beam', 'Stair', 'StairFlight', 'Railing', 'Covering', 'FurnishingElement', 'Furniture', 'Member', 'Plate', 'CurtainWall',
  'BuildingElementProxy', 'Footing', 'Pile', 'Ramp', 'RampFlight', 'OpeningElement', 'FlowTerminal', 'FlowSegment', 'FlowFitting', 'SanitaryTerminal', 'DistributionElement', 'Chimney', 'ShadingDevice', 'ReinforcingBar', 'ElementAssembly', 'BuildingElementPart', 'GeographicElement']
  .forEach(n => IFC_CC['IFC' + n.toUpperCase()] = 'Ifc' + n);
const ifcClass = t => { const u = String(t || '').toUpperCase(); return IFC_CC[u] || (u.startsWith('IFC') ? 'Ifc' + u.slice(3).charAt(0) + u.slice(4).toLowerCase() : String(t || '')); };
const iv = v => v == null ? null : typeof v !== 'object' ? v : Array.isArray(v) ? v.map(iv) : (v.value !== undefined ? v.value : v._representationValue !== undefined ? v._representationValue : null);

async function parseIFC(bytes, onStep = () => {}){
  onStep('Chargement du lecteur IFC…');
  const {api, W} = await ifcApi();
  onStep('Lecture du fichier IFC…'); await frame();
  let mid;
  try { mid = api.OpenModel(bytes, {COORDINATE_TO_ORIGIN:true, USE_FAST_BOOLS:true}); } catch(e){ throw new Error('Fichier IFC illisible.'); }
  if (mid < 0) throw new Error('Fichier IFC illisible.');
  try {
    /* 1. géométrie, regroupée par élément puis par couleur */
    onStep('Calcul de la géométrie…'); await frame();
    const byEl = new Map(), mats = new Map();
    const matFor = c => { const k = [c.x, c.y, c.z, c.w].map(v => v.toFixed(3)).join(); let m = mats.get(k); if (!m){ m = new THREE.MeshStandardMaterial({color:new THREE.Color(c.x, c.y, c.z).convertSRGBToLinear(), roughness:0.8, metalness:0, transparent:c.w < 0.99, opacity:c.w, side:THREE.DoubleSide, depthWrite:c.w >= 0.99}); m.name = c.w < 0.99 ? 'Vitrage' : 'IFC ' + k; mats.set(k, m); } return m; };
    let nGeo = 0;
    api.StreamAllMeshes(mid, mesh => {
      const eid = mesh.expressID, pgs = mesh.geometries; let slot = byEl.get(eid); if (!slot) byEl.set(eid, slot = new Map());
      for (let i = 0; i < pgs.size(); i++){
        const pg = pgs.get(i), geo = api.GetGeometry(mid, pg.geometryExpressID);
        const v = api.GetVertexArray(geo.GetVertexData(), geo.GetVertexDataSize()), idx = api.GetIndexArray(geo.GetIndexData(), geo.GetIndexDataSize());
        const n = v.length / 6, pos = new Float32Array(n * 3), nor = new Float32Array(n * 3);
        for (let k = 0; k < n; k++){ pos[k * 3] = v[k * 6]; pos[k * 3 + 1] = v[k * 6 + 1]; pos[k * 3 + 2] = v[k * 6 + 2]; nor[k * 3] = v[k * 6 + 3]; nor[k * 3 + 1] = v[k * 6 + 4]; nor[k * 3 + 2] = v[k * 6 + 5]; }
        const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pos, 3)); g.setAttribute('normal', new THREE.BufferAttribute(nor, 3)); g.setIndex(new THREE.BufferAttribute(new Uint32Array(idx), 1));
        g.applyMatrix4(new THREE.Matrix4().fromArray(pg.flatTransformation));
        const m = matFor(pg.color); let arr = slot.get(m); if (!arr) slot.set(m, arr = []); arr.push(g);
        geo.delete?.(); nGeo++;
      }
    });
    if (!nGeo) throw new Error('Ce fichier IFC ne contient aucune géométrie affichable.');
    /* 2. relations : propriétés, matériaux, types (une seule passe) */
    onStep('Lecture des propriétés BIM…'); await frame();
    const lines = t => { try { const v = api.GetLineIDsWithType(mid, t), out = []; for (let i = 0; i < v.size(); i++) out.push(v.get(i)); return out; } catch(e){ return []; } };
    const get = (id, flat = false) => { try { return api.GetLine(mid, id, flat); } catch(e){ return null; } };
    const psetOf = new Map(), matOf = new Map(), typeOf = new Map();
    const push = (map, k, v) => { let a = map.get(k); if (!a) map.set(k, a = []); a.push(v); };
    for (const rid of lines(W.IFCRELDEFINESBYPROPERTIES)){ const r = get(rid); const d = r?.RelatingPropertyDefinition?.value; if (d) for (const o of r.RelatedObjects || []) push(psetOf, o.value, d); }
    for (const rid of lines(W.IFCRELASSOCIATESMATERIAL)){ const r = get(rid); const d = r?.RelatingMaterial?.value; if (d) for (const o of r.RelatedObjects || []) push(matOf, o.value, d); }
    for (const rid of lines(W.IFCRELDEFINESBYTYPE)){ const r = get(rid); const d = r?.RelatingType?.value; if (d) for (const o of r.RelatedObjects || []) typeOf.set(o.value, d); }
    const readPset = pid => {
      const p = get(pid, true); if (!p) return null; const out = {};
      for (const h of p.HasProperties || []){ const nm = iv(h?.Name); if (!nm) continue; const val = h.NominalValue !== undefined ? iv(h.NominalValue) : h.EnumerationValues ? iv(h.EnumerationValues).join(', ') : h.ListValues ? iv(h.ListValues).join(', ') : null; if (val != null && val !== '') out[nm] = val; }
      for (const q of p.Quantities || []){ const nm = iv(q?.Name); if (!nm) continue; const val = iv(q.LengthValue ?? q.AreaValue ?? q.VolumeValue ?? q.CountValue ?? q.WeightValue ?? q.TimeValue); if (val != null) out[nm] = typeof val === 'number' ? Math.round(val * 1000) / 1000 : val; }
      return {name:iv(p.Name) || 'Propriétés', props:out};
    };
    const readMat = mid_ => {
      const m = get(mid_, true); if (!m) return {};
      const layerSet = m.ForLayerSet || (m.MaterialLayers ? m : null);
      if (layerSet?.MaterialLayers) return {couches:layerSet.MaterialLayers.map(l => ({materiau:iv(l.Material?.Name) || iv(l.Name) || '—', epaisseur:Math.round((iv(l.LayerThickness) || 0) * 1000) / 1000}))};
      if (m.Materials) return {materiaux:m.Materials.map(x => iv(x?.Name)).filter(Boolean)};
      if (m.MaterialConstituents) return {materiaux:m.MaterialConstituents.map(x => iv(x?.Material?.Name) || iv(x?.Name)).filter(Boolean)};
      if (m.Name) return {materiaux:[iv(m.Name)]};
      return {};
    };
    const info = (eid, type) => {
      const l = get(eid) || {}, cls = ifcClass(type || api.GetNameFromTypeCode?.(api.GetLineType(mid, eid)));
      const d = {expressID:eid, classe:cls, nom:iv(l.Name) || '', globalId:iv(l.GlobalId) || ''};
      const desc = iv(l.Description), ot = iv(l.ObjectType), tag = iv(l.Tag), ln = iv(l.LongName), el = iv(l.Elevation);
      if (desc) d.description = desc; if (ot) d.typeObjet = ot; if (tag) d.reference = tag; if (ln) d.nomLong = ln; if (el != null && typeof el === 'number') d.altitude = Math.round(el * 1000) / 1000;
      const pd = iv(l.PredefinedType); if (pd && pd !== 'NOTDEFINED' && pd !== 'USERDEFINED') d.typePredefini = pd;
      const ty = typeOf.get(eid); if (ty){ const t = get(ty); if (t) d.type = iv(t.Name) || ifcClass(api.GetNameFromTypeCode?.(api.GetLineType(mid, ty))); for (const hp of t?.HasPropertySets || []) push(psetOf, eid, hp.value); }
      for (const m of matOf.get(eid) || []) Object.assign(d, readMat(m));
      const ps = {}; for (const pid of psetOf.get(eid) || []){ const r = readPset(pid); if (r && Object.keys(r.props).length) ps[r.name] = {...(ps[r.name] || {}), ...r.props}; }
      if (Object.keys(ps).length) d.psets = ps;
      return d;
    };
    /* 3. arborescence spatiale : Projet > Site > Bâtiment > Niveaux > éléments */
    onStep('Construction de la hiérarchie…'); await frame();
    let tree = null; try { tree = await api.properties.getSpatialStructure(mid, false); } catch(e){}
    const root = new THREE.Group(); root.name = 'Maquette IFC';
    const used = new Set();
    /* fichier sans couleurs (un ou deux styles pour toute la maquette) : teintes par classe, comme ARCHIPLAN */
    const uniform = mats.size <= 2, CLS = {}, CLS_COL = {IfcWall:0xEDE6DA, IfcWallStandardCase:0xEDE6DA, IfcSlab:0xBDBDB8, IfcRoof:0xA6533A, IfcCovering:0xD9D3C7, IfcDoor:0x8B6A4E, IfcWindow:0x2B3440, IfcColumn:0xB0B3B5, IfcBeam:0xB0B3B5, IfcStair:0xC9C2B5, IfcStairFlight:0xC9C2B5, IfcRailing:0x4A5560, IfcFurnishingElement:0xB98B5E, IfcFurniture:0xB98B5E, IfcSpace:0x7FE0CF, IfcFooting:0x9E9E99, IfcPlate:0x9FC9E0, IfcMember:0x8A939B};
    const clsMat = (cls, m) => { if (!uniform || m.transparent) return m; const c = CLS_COL[cls]; if (c == null) return m; return CLS[cls] || (CLS[cls] = Object.assign(new THREE.MeshStandardMaterial({color:c, roughness:0.8, metalness:0, side:THREE.DoubleSide}), {name:IFC_FR[cls.toUpperCase()] || cls})); };
    const meshesFor = (eid, g, label, cls) => {
      const slot = byEl.get(eid); if (!slot) return 0; used.add(eid); let n = 0;
      for (const [m0, geos] of slot){ const m = clsMat(cls, m0); const geo = geos.length > 1 ? BufferGeometryUtils.mergeGeometries(geos, false) : geos[0]; if (geos.length > 1) geos.forEach(x => x.dispose()); const me = new THREE.Mesh(geo, m); me.name = `${label} · ${m.name}`; g.add(me); n++; }
      return n;
    };
    const build = (node, parent) => {
      const cls = ifcClass(node.type), d = info(node.expressID, node.type);
      const g = new THREE.Group(); g.name = d.nom || d.nomLong || IFC_FR[String(node.type).toUpperCase()] || cls; g.userData.ifc = d;
      if (cls === 'IfcSpace' || cls === 'IfcOpeningElement') g.visible = false;
      meshesFor(node.expressID, g, g.name, d.classe);
      for (const c of node.children || []) build(c, g);
      if (g.children.length || ['IfcProject', 'IfcSite', 'IfcBuilding', 'IfcBuildingStorey'].includes(cls)) parent.add(g);
    };
    if (tree) build(tree, root);
    const rest = [...byEl.keys()].filter(e => !used.has(e));
    if (rest.length){ const other = new THREE.Group(); other.name = 'Autres'; other.userData.bim = {groupe:'Éléments hors structure spatiale'};
      for (const eid of rest){ const d = info(eid); const g = new THREE.Group(); g.name = d.nom || IFC_FR[d.classe.toUpperCase()] || d.classe; g.userData.ifc = d; if (d.classe === 'IfcSpace' || d.classe === 'IfcOpeningElement') g.visible = false; meshesFor(eid, g, g.name, d.classe); other.add(g); }
      (root.children[0] || root).add(other); }
    const proj = root.children[0]?.userData?.ifc; if (proj) root.userData.ifcProjet = {nom:proj.nom || proj.nomLong || '', schema:api.GetModelSchema?.(mid) || ''};
    return root;
  } finally { try { api.CloseModel(mid); } catch(e){} }
}

/* ---------- ZIP (paquet ARCHIPLAN) : lecture des entrées, stockées ou compressées (deflate) ---------- */
async function unzip(buf){
  const dv = new DataView(buf), u8 = new Uint8Array(buf); let e = buf.byteLength - 22;
  while (e >= 0 && dv.getUint32(e, true) !== 0x06054b50) e--; if (e < 0) throw new Error('Archive ZIP illisible.');
  const n = dv.getUint16(e + 10, true); let p = dv.getUint32(e + 16, true); const out = [];
  for (let i = 0; i < n; i++){
    if (dv.getUint32(p, true) !== 0x02014b50) break;
    const meth = dv.getUint16(p + 10, true), csz = dv.getUint32(p + 20, true), nl = dv.getUint16(p + 28, true), xl = dv.getUint16(p + 30, true), cl = dv.getUint16(p + 32, true), lo = dv.getUint32(p + 42, true);
    const name = new TextDecoder().decode(u8.subarray(p + 46, p + 46 + nl)); p += 46 + nl + xl + cl;
    const ds = lo + 30 + dv.getUint16(lo + 26, true) + dv.getUint16(lo + 28, true);
    out.push({name, get:async () => { const raw = u8.slice(ds, ds + csz); if (meth === 0) return raw.buffer; if (meth === 8 && 'DecompressionStream' in window) return new Response(new Blob([raw]).stream().pipeThrough(new DecompressionStream('deflate-raw'))).arrayBuffer(); throw new Error('Compression ZIP non prise en charge.'); }});
  }
  return out;
}

/* ---------- Import : fichiers choisis → enregistrement sur l'appareil ---------- */
const extOf = n => (n.split('.').pop() || '').toLowerCase();
const baseName = n => n.replace(/\.[^.]+$/, '').replace(/_archivision$/i, '').replace(/[_]+/g, ' ').trim() || 'Maquette';
async function importFiles(files, onStep = () => {}){
  const user = window.AVUser?.(); if (!user) throw new Error('Connectez-vous d\'abord.');
  let main = files.find(f => extOf(f.name) === 'glb') || files.find(f => extOf(f.name) === 'gltf') || files.find(f => extOf(f.name) === 'ifc') || files.find(f => extOf(f.name) === 'zip');
  if (!main) throw new Error('Format non pris en charge. Choisissez un fichier .ifc, .gltf, .glb ou .zip.');
  let kind = extOf(main.name), name = baseName(main.name), root, source = null, glb = null, size = main.size;
  if (kind === 'zip'){
    onStep('Ouverture de l\'archive…');
    const ents = await unzip(await main.arrayBuffer());
    const e = ents.find(x => extOf(x.name) === 'glb') || ents.find(x => extOf(x.name) === 'ifc');
    if (!e) throw new Error('L\'archive ne contient ni GLB ni IFC.');
    const data = await e.get(); kind = extOf(e.name); name = baseName(e.name.split('/').pop());
    main = new File([data], e.name.split('/').pop(), {type:kind === 'glb' ? 'model/gltf-binary' : 'application/x-step'}); size = main.size;
  }
  if (kind === 'glb'){ onStep('Lecture du GLB…'); const buf = await main.arrayBuffer(); root = await parseGLB(buf); glb = new Blob([buf], {type:'model/gltf-binary'}); }
  else if (kind === 'gltf'){ onStep('Lecture du glTF…'); root = await parseGLTF(await main.text(), files.filter(f => f !== main)); onStep('Assemblage en GLB…'); glb = await exportGLB(root); kind = 'gltf'; }
  else if (kind === 'ifc'){
    const bytes = new Uint8Array(await main.arrayBuffer()); source = new Blob([bytes], {type:'application/x-step'});
    root = await parseIFC(bytes, onStep);
    if (root.userData.ifcProjet?.nom && name === 'Maquette') name = root.userData.ifcProjet.nom;
    onStep('Conversion en GLB (une seule fois)…'); await frame(); glb = await exportGLB(root);
  }
  if (root.userData?.archiplan?.projet && /^(maquette|projet)$/i.test(name)) name = root.userData.archiplan.projet;
  const rec = {id:window.AVStore.newId('m_'), uid:user.uid, name, kind, size, glb, source, createdAt:Date.now(), openedAt:Date.now(), unitScale:0};
  onStep('Aperçu…');
  setModel(root, rec); rec.unitScale = V.unitScale; await frame();
  rec.thumb = await thumbnail(); rec.stats = {objets:V.meshes.length, triangles:Math.round(V.tris), dims:[V.size.x, V.size.y, V.size.z].map(x => Math.round(x * 100) / 100)};
  onStep('Enregistrement sur l\'appareil…');
  try { await window.AVStore.putModel(rec); }
  catch(e){ throw new Error(e?.name === 'QuotaExceededError' ? 'Plus de place sur l\'appareil : supprimez des maquettes ou des photos.' : 'Impossible d\'enregistrer la maquette sur l\'appareil.'); }
  window.AVStore.persist();
  const {glb:_, source:__, ...meta} = rec; return meta;
}
