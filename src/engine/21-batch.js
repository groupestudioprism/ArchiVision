/* ============ Affichage groupé : bien moins d'appels de dessin sur les grosses maquettes ============
   Les pièces opaques visibles sont fusionnées par matériau, par paquets de BATCH_N objets, dans un groupe qui suit la
   maquette (visite, unités, réalité augmentée). Les objets d'origine restent dans la maquette, avec leur visibilité, leurs
   données BIM et la sélection ; ils passent seulement sur le calque de rendu 1, que la caméra ne dessine pas mais que les
   rayons (sélection, mesure, marche) voient toujours. Le paquet qui contient la sélection est affiché en direct.
   Transparents (vitrages, rayons X) jamais fusionnés. Regroupement refait quand la visibilité, le style ou les ombres changent. */
const BATCH_N = 320, BATCH = {on:true, chunks:[], key:'', soon:false};
const batchGrp = new THREE.Group(); batchGrp.name = 'affichage-groupe'; batchGrp.matrixAutoUpdate = false; scene.add(batchGrp);
const BATCH_INV = new THREE.Matrix4(), BATCH_M = new THREE.Matrix4(), BATCH_N3 = new THREE.Matrix3(), BATCH_V = new THREE.Vector3();
function batchClear(){
  for (const ch of BATCH.chunks){ for (const o of ch.objs) o.geometry.dispose(); for (const m of ch.meshes) m.layers.set(0); }
  batchGrp.clear(); BATCH.chunks = [];
}
const batchKey = () => V.style + '|' + V.opts.shadows;
const batchSoon = () => { BATCH.soon = true; markDirty(); };
function batchable(o, M){
  if (o.isInstancedMesh || o.isSkinnedMesh || o.morphTargetInfluences || Array.isArray(M) || !M || M.transparent || M.visible === false) return false;
  const g = o.geometry; return !!(g && g.attributes.position && !g.morphAttributes?.position && !g.groups?.length);
}
function batchMergeGeo(list){
  const names = Object.keys(list[0].g.attributes); let n = 0;
  for (const {g} of list) n += g.index ? g.index.count : g.attributes.position.count;
  const out = {};
  for (const k of names){ const a = list[0].g.attributes[k]; out[k] = {size:a.itemSize, arr:new Float32Array(n * a.itemSize)}; }
  let at = 0;
  for (const {g, m} of list){
    const idx = g.index, c = idx ? idx.count : g.attributes.position.count;
    for (const k of names){
      const a = g.attributes[k], O = out[k], s = O.size;
      for (let i = 0; i < c; i++){ const j = idx ? idx.getX(i) : i; for (let q = 0; q < s; q++) O.arr[(at + i) * s + q] = a.getComponent(j, q); }
    }
    BATCH_N3.getNormalMatrix(m);
    const P = out.position.arr, N = out.normal?.arr;
    for (let i = at; i < at + c; i++){
      BATCH_V.fromArray(P, i * 3).applyMatrix4(m).toArray(P, i * 3);
      if (N) BATCH_V.fromArray(N, i * 3).applyMatrix3(BATCH_N3).normalize().toArray(N, i * 3);
    }
    at += c;
  }
  const G = new THREE.BufferGeometry();
  for (const k of names){ const a = list[0].g.attributes[k]; G.setAttribute(k, new THREE.BufferAttribute(out[k].arr, out[k].size, a.normalized)); }
  G.computeBoundingSphere(); return G;
}
function batchBuild(){
  BATCH.soon = false; batchClear(); BATCH.key = batchKey();
  if (!BATCH.on || !V.root || !V.meshes.length) return;
  V.root.updateWorldMatrix(true, true); BATCH_INV.copy(V.root.matrixWorld).invert();
  const list = V.meshes.filter(o => isShown(o));
  for (let i = 0; i < list.length; i += BATCH_N){
    const ch = {meshes:[], objs:[], grp:new THREE.Group(), live:false}, buckets = new Map();
    ch.grp.matrixAutoUpdate = false;
    for (const o of list.slice(i, i + BATCH_N)){
      const M = styleMat(o.userData.__orig);
      if (!batchable(o, M)) continue;
      const sig = Object.keys(o.geometry.attributes).sort().map(k => k + o.geometry.attributes[k].itemSize).join(',');
      const key = M.uuid + '|' + (o.castShadow ? 1 : 0) + (o.receiveShadow ? 1 : 0) + '|' + sig;
      let b = buckets.get(key); if (!b) buckets.set(key, b = {M, cast:o.castShadow, recv:o.receiveShadow, list:[]});
      b.list.push({g:o.geometry, m:BATCH_M.clone().multiplyMatrices(BATCH_INV, o.matrixWorld)}); ch.meshes.push(o);
    }
    for (const b of buckets.values()){
      const mesh = new THREE.Mesh(batchMergeGeo(b.list), b.M); mesh.castShadow = b.cast; mesh.receiveShadow = b.recv; mesh.raycast = () => {}; mesh.matrixAutoUpdate = false;
      ch.grp.add(mesh); ch.objs.push(mesh);
    }
    for (const o of ch.meshes) o.layers.set(1);
    batchGrp.add(ch.grp); BATCH.chunks.push(ch);
  }
  batchLive(); markDirty();
}
/* paquet contenant la sélection : objets d'origine affichés (surbrillance), paquet masqué */
function batchLive(){
  for (const ch of BATCH.chunks){
    let live = false; for (const o of ch.meshes) if (V.selMeshes.has(o)){ live = true; break; }
    if (live === ch.live) continue;
    ch.live = live; ch.grp.visible = !live; for (const o of ch.meshes) o.layers.set(live ? 0 : 1);
  }
}
/* avant chaque image : le groupe suit la maquette (unités, réalité augmentée) */
function batchSync(){
  if (BATCH.soon) batchBuild();
  if (!V.root || !BATCH.chunks.length){ batchGrp.visible = false; return; }
  batchGrp.visible = true; V.root.updateWorldMatrix(true, false); batchGrp.matrix.copy(V.root.matrixWorld); batchGrp.matrixWorldNeedsUpdate = true;
}
