/* ============ Vue 3D : scène, lumières, sol, styles d'affichage, sélection, rendu à la demande ============ */
const canvas = $('#c'), vpEl = $('#vp');
const renderer = new THREE.WebGLRenderer({canvas, antialias:true, alpha:true, powerPreference:'high-performance'});
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = 0.82;
renderer.shadowMap.enabled = true; renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.xr.enabled = true;

const scene = new THREE.Scene();
const pmrem = new THREE.PMREMGenerator(renderer);
scene.environment = pmrem.fromScene(new RoomEnvironment(renderer), 0.04).texture;
const camera = new THREE.PerspectiveCamera(50, 1, 0.05, 5000);
camera.position.set(12, 9, 14);
const controls = new OrbitControls(camera, canvas);
controls.enableDamping = true; controls.dampingFactor = 0.12; controls.screenSpacePanning = true;
controls.maxPolarAngle = Math.PI * 0.495;

const hemi = new THREE.HemisphereLight(0xffffff, 0x8a96a3, 0.4); scene.add(hemi);
const sun = new THREE.DirectionalLight(0xfff6ea, 1.35); sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048); sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.02;
scene.add(sun, sun.target);

/* sol : grille + plan qui ne reçoit que les ombres (gardé en AR pour poser la maquette) */
const ground = new THREE.Group(); scene.add(ground);
const shadowPlane = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.ShadowMaterial({opacity:0.18}));
shadowPlane.rotation.x = -Math.PI / 2; shadowPlane.receiveShadow = true; shadowPlane.renderOrder = -1; ground.add(shadowPlane);
let grid = null;

/* pivot : la maquette est posée dessus (base au centre, en y = 0) ; c'est lui qu'on ancre en AR */
const pivot = new THREE.Group(); pivot.name = 'ARCHIVISION'; scene.add(pivot);

const V = {
  root:null, rec:null, meshes:[], box:new THREE.Box3(), size:new THREE.Vector3(1, 1, 1), radius:1,
  style:'mat', tool:'orbit', sel:null, selMeshes:new Set(), opts:{shadows:true, edges:false, ground:true}, dirty:2, mode:'tour', unitScale:1,
};
const markDirty = () => { V.dirty = Math.max(V.dirty, 2); };
controls.addEventListener('change', markDirty);

function themeBg(){ return document.documentElement.dataset.theme === 'dark' ? 0x11161E : 0xF2F6F9; }
function applyTheme(){
  scene.background = V.mode === 'ar' ? null : new THREE.Color(themeBg());
  if (grid){ const dark = document.documentElement.dataset.theme === 'dark'; grid.material.color?.set?.(dark ? 0x2A3443 : 0xC9D6E0); }
  shadowPlane.material.opacity = document.documentElement.dataset.theme === 'dark' ? 0.35 : 0.18; markDirty();
}
new MutationObserver(applyTheme).observe(document.documentElement, {attributes:true, attributeFilter:['data-theme']});
applyTheme();

function resize(){
  if (renderer.xr.isPresenting) return;
  const r = vpEl.getBoundingClientRect(); if (!r.width || !r.height) return;
  renderer.setSize(r.width, r.height, false); camera.aspect = r.width / r.height; camera.updateProjectionMatrix(); markDirty();
}
new ResizeObserver(resize).observe(vpEl);

/* ---------- Matériaux : d'origine, styles, surbrillance ---------- */
const STYLE_MATS = new Map(), HL_MATS = new Map();
const xrayMat = new THREE.MeshBasicMaterial({color:0x2A7C93, transparent:true, opacity:0.10, depthWrite:false, side:THREE.DoubleSide});
const wireMat = new THREE.MeshBasicMaterial({color:0x1F5A86, wireframe:true});
const edgeMat = new THREE.LineBasicMaterial({color:0x0B1F45, transparent:true, opacity:0.45});
function baseColor(m){ const c = (Array.isArray(m) ? m[0] : m)?.color; return c ? c.clone() : new THREE.Color(0xd0d4d8); }
function styleMat(orig){
  if (V.style === 'mat') return orig;
  if (V.style === 'wire') return wireMat;
  if (V.style === 'xray') return xrayMat;
  const key = (Array.isArray(orig) ? orig.map(m => m.uuid).join() : orig.uuid);
  let m = STYLE_MATS.get(key);
  if (!m){ const c = baseColor(orig).lerp(new THREE.Color(0xffffff), 0.25); m = new THREE.MeshStandardMaterial({color:c, roughness:0.85, metalness:0, side:THREE.DoubleSide, polygonOffset:true, polygonOffsetFactor:1, polygonOffsetUnits:1}); STYLE_MATS.set(key, m); }
  return m;
}
function hlMat(base){
  const one = Array.isArray(base) ? base[0] : base;
  let m = HL_MATS.get(one.uuid);
  if (!m){
    m = one.isMeshBasicMaterial && one.wireframe ? new THREE.MeshBasicMaterial({color:0x2FD3B4, wireframe:true}) : (one.clone ? one.clone() : new THREE.MeshStandardMaterial());
    if (m.emissive){ m.color = (m.color || new THREE.Color()).clone().lerp(new THREE.Color(0x14B8A0), 0.55); m.emissive = new THREE.Color(0x0E8C78); m.emissiveIntensity = 0.35; m.map = m.map || null; } else if (m.color) m.color = new THREE.Color(0x2FD3B4);
    if (one === xrayMat){ m.opacity = 0.75; m.color = new THREE.Color(0x2FD3B4); m.depthWrite = true; }
    HL_MATS.set(one.uuid, m);
  }
  return Array.isArray(base) ? base.map(() => m) : m;
}
function refreshMaterials(){
  for (const o of V.meshes){
    const base = styleMat(o.userData.__orig);
    o.material = V.selMeshes.has(o) ? hlMat(base) : base;
    o.castShadow = V.opts.shadows && V.style !== 'xray' && V.style !== 'wire';
    o.receiveShadow = V.opts.shadows && V.style === 'mat';
  }
  updateEdges(); markDirty();
}
function setStyle(s){ V.style = s; $$('#styleGrp [data-style]').forEach(b => b.setAttribute('aria-pressed', b.dataset.style === s)); refreshMaterials(); }

/* arêtes : calculées à la demande (lourd sur les grosses maquettes) */
const EDGE_LIMIT = 1.5e6;
function updateEdges(){
  const want = (V.opts.edges || V.style === 'xray' || V.style === 'shaded') && V.tris < EDGE_LIMIT;
  for (const o of V.meshes){
    let e = o.userData.__edges;
    if (want && !e){ try { e = new THREE.LineSegments(new THREE.EdgesGeometry(o.geometry, 30), edgeMat); e.raycast = () => {}; e.userData.__helper = true; o.add(e); o.userData.__edges = e; } catch(err){ o.userData.__edges = e = null; } }
    if (e) e.visible = want;
  }
  edgeMat.color.set(document.documentElement.dataset.theme === 'dark' && V.style !== 'shaded' ? 0x9FC3E6 : 0x0B1F45);
}

/* ---------- Chargement d'une maquette dans la scène ---------- */
function disposeObj(root){
  root.traverse(o => {
    if (o.geometry) o.geometry.dispose();
    const ms = o.userData?.__orig ? [o.userData.__orig].flat() : o.material ? [o.material].flat() : [];
    for (const m of ms){ for (const k in m){ const v = m[k]; if (v && v.isTexture) v.dispose(); } m.dispose?.(); }
  });
}
function setModel(root, rec){
  if (V.root){ pivot.remove(V.root); disposeObj(V.root); }
  STYLE_MATS.forEach(m => m.dispose()); STYLE_MATS.clear(); HL_MATS.forEach(m => m.dispose()); HL_MATS.clear();
  clearMeasures(); setSection(null);
  V.root = root; V.rec = rec; V.sel = null; V.selMeshes.clear(); V.meshes = []; V.tris = 0;
  root.traverse(o => {
    if (o.isMesh && !o.userData.__helper){
      o.userData.__orig = o.material; V.meshes.push(o);
      const g = o.geometry; V.tris += g.index ? g.index.count / 3 : (g.attributes.position?.count || 0) / 3;
      if (!g.attributes.normal) g.computeVertexNormals();
    }
  });
  pivot.add(root);
  normalizeModel(rec?.unitScale);
  refreshMaterials(); applyTheme();
  setView('iso', true); select(null);
  $('#vpEmpty').hidden = true;
  $('#statOut').textContent = `${nf(V.size.x, 1)} × ${nf(V.size.z, 1)} × ${nf(V.size.y, 1)} m · ${V.meshes.length} objets`;
  markDirty();
}
/* base de la maquette au centre, en y = 0 ; unités : mètres (conversion des maquettes en mm ou cm) */
function guessUnitScale(box){ const s = box.getSize(new THREE.Vector3()), m = Math.max(s.x, s.y, s.z); return m > 3000 ? 0.001 : m > 600 ? 0.01 : 1; }
function normalizeModel(forced){
  const root = V.root; root.position.set(0, 0, 0); root.scale.setScalar(1); root.updateMatrixWorld(true);
  const raw = new THREE.Box3().setFromObject(root);
  if (raw.isEmpty()) raw.set(new THREE.Vector3(-1, 0, -1), new THREE.Vector3(1, 1, 1));
  const k = forced || guessUnitScale(raw); V.unitScale = k;
  root.scale.setScalar(k);
  const c = raw.getCenter(new THREE.Vector3());
  root.position.set(-c.x * k, -raw.min.y * k, -c.z * k); root.updateMatrixWorld(true);
  V.box.setFromObject(root); V.box.getSize(V.size); V.radius = Math.max(0.5, V.size.length() / 2);
  // sol et ombres à la taille de la maquette
  const S = Math.max(V.size.x, V.size.z) * 3 + 4;
  shadowPlane.scale.set(S, S, 1);
  if (grid){ ground.remove(grid); grid.geometry.dispose(); }
  const step = S > 400 ? 10 : S > 80 ? 5 : 1, n = Math.max(4, Math.round(S / step));
  grid = new THREE.GridHelper(n * step, n, 0xC9D6E0, 0xC9D6E0); grid.material.transparent = true; grid.material.opacity = 0.55; grid.position.y = 0.002; ground.add(grid);
  ground.visible = V.opts.ground;
  const r = V.radius;
  sun.position.set(r * 1.2, r * 2.2, r * 0.9); sun.target.position.set(0, 0, 0);
  const sc = sun.shadow.camera; sc.left = sc.bottom = -r * 1.4; sc.right = sc.top = r * 1.4; sc.near = 0.1; sc.far = r * 6; sc.updateProjectionMatrix();
  camera.near = Math.max(0.01, r / 2000); camera.far = r * 60; camera.updateProjectionMatrix();
  controls.maxDistance = r * 12; controls.minDistance = Math.min(0.2, r / 20);
  applyTheme();
}

/* ---------- Caméras ---------- */
function setView(kind, instant){
  if (V.tool === 'walk' || V.tool === 'pano') setTool('orbit');
  const r = V.radius, c = new THREE.Vector3(0, V.size.y / 2, 0), fov = THREE.MathUtils.degToRad(camera.fov);
  const d = r / Math.sin(fov / 2) * (camera.aspect < 1 ? 1.25 / Math.max(camera.aspect, 0.5) : 1.05);
  const dir = kind === 'top' ? new THREE.Vector3(0.0001, 1, 0.0001) : kind === 'front' ? new THREE.Vector3(0, 0.12, 1) : new THREE.Vector3(1, 0.75, 1.15);
  const to = c.clone().add(dir.normalize().multiplyScalar(d));
  flyTo(to, c, instant ? 0 : 450);
}
let flight = null;
function flyTo(pos, target, ms = 450){
  if (!ms){ camera.position.copy(pos); controls.target.copy(target); controls.update(); markDirty(); return; }
  flight = {t0:performance.now(), ms, p0:camera.position.clone(), t0v:controls.target.clone(), p1:pos.clone(), t1:target.clone()};
}
function stepFlight(now){
  if (!flight) return;
  const k = clamp((now - flight.t0) / flight.ms, 0, 1), e = k < .5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2;
  camera.position.lerpVectors(flight.p0, flight.p1, e); controls.target.lerpVectors(flight.t0v, flight.t1, e); controls.update();
  if (k >= 1) flight = null; markDirty();
}
function focusObject(o){
  const b = new THREE.Box3().setFromObject(o); if (b.isEmpty()) return;
  const c = b.getCenter(new THREE.Vector3()), r = Math.max(0.4, b.getSize(new THREE.Vector3()).length() / 2);
  if (V.tool === 'walk' || V.tool === 'pano') setTool('orbit');
  const dir = camera.position.clone().sub(controls.target).normalize();
  flyTo(c.clone().add(dir.multiplyScalar(r / Math.sin(THREE.MathUtils.degToRad(camera.fov) / 2) * 1.1)), c);
}

/* ---------- Sélection ---------- */
const ray = new THREE.Raycaster(), ndc = new THREE.Vector2();
function pickAt(clientX, clientY){
  if (!V.root) return null;
  const r = canvas.getBoundingClientRect(); ndc.set((clientX - r.left) / r.width * 2 - 1, -(clientY - r.top) / r.height * 2 + 1);
  ray.setFromCamera(ndc, camera);
  return pickRay(ray);
}
function pickRay(rc){
  const hits = rc.intersectObjects(V.meshes.filter(isShown), false);
  const clip = renderer.clippingPlanes[0];
  return hits.find(h => !clip || clip.distanceToPoint(h.point) >= -1e-4) || null;
}
function isShown(o){ for (let p = o; p; p = p.parent){ if (!p.visible) return false; if (p === V.root) break; } return true; }
const hasInfo = o => !!(o.userData && (o.userData.archiplan || o.userData.ifc || o.userData.bim));
/* élément BIM d'un maillage : l'ancêtre le plus proche qui porte des données (ARCHIPLAN, IFC) ; sinon le maillage */
function elementOf(mesh){ for (let p = mesh; p && p !== V.root; p = p.parent){ if (hasInfo(p) && !(p.userData.archiplan && !p.userData.archiplan.type && !p.userData.archiplan.id && p.userData.archiplan.version)) return p; } return mesh; }
function select(obj, {focus = false, from = 'view'} = {}){
  V.sel = obj || null; V.selMeshes.clear();
  if (obj) obj.traverse(o => { if (o.isMesh && o.userData.__orig) V.selMeshes.add(o); });
  refreshMaterials();
  const b = $('#selBadge'); b.hidden = !obj; if (obj) b.textContent = objLabel(obj);
  if (obj && focus) focusObject(obj);
  window.dispatchEvent(new CustomEvent('av:select', {detail:{obj, from}}));
}

/* ---------- Visibilité (hiérarchie) ---------- */
function setVisible(o, v){ o.visible = v; markDirty(); }
function isolate(o){
  V.root.traverse(x => { if (x !== V.root && !x.userData.__helper) x.visible = false; });
  for (let p = o; p && p !== V.root; p = p.parent) p.visible = true;
  o.traverse(x => { if (!x.userData.__helper) x.visible = true; }); markDirty();
}
function showAll(){ V.root?.traverse(x => { if (!x.userData.__helper || x.userData.__edges !== undefined) x.visible = true; }); updateEdges(); markDirty(); }

/* ---------- Boucle de rendu (à la demande hors AR) ---------- */
let lastT = performance.now();
const tickers = new Set();
renderer.setAnimationLoop((now, xrFrame) => {
  const dt = Math.min(0.1, (now - lastT) / 1000); lastT = now;
  stepFlight(now);
  for (const f of tickers) f(dt, now, xrFrame);
  if (V.tool === 'orbit' || V.tool === 'inspect' || V.tool === 'measure' || V.tool === 'section'){ if (controls.update()) V.dirty = Math.max(V.dirty, 1); }
  if (renderer.xr.isPresenting || V.dirty > 0){ renderer.render(scene, camera); if (!renderer.xr.isPresenting) placeLabels(); V.dirty = Math.max(0, V.dirty - 1); }
});

/* ---------- Capture de la vue (HD) ---------- */
async function captureView({scale = 2, mime = 'image/jpeg', quality = 0.9, stamp = true} = {}){
  const w0 = canvas.clientWidth, h0 = canvas.clientHeight, pr0 = renderer.getPixelRatio();
  const pr = Math.min(scale * 1, 4096 / Math.max(w0, h0)), sel = V.sel;
  if (sel) select(null);
  renderer.setPixelRatio(pr); renderer.setSize(w0, h0, false);
  renderer.render(scene, camera);
  const out = document.createElement('canvas'); out.width = canvas.width; out.height = canvas.height;
  const g = out.getContext('2d'); g.drawImage(canvas, 0, 0);
  renderer.setPixelRatio(pr0); renderer.setSize(w0, h0, false); if (sel) select(sel); markDirty();
  drawMeasuresOn(g, out.width / w0);
  if (stamp) drawStamp(g, out.width, out.height);
  const blob = await new Promise(r => out.toBlob(r, mime, quality));
  return {blob, w:out.width, h:out.height, canvas:out};
}
function drawStamp(g, W, H){
  const s = Math.max(1, W / 1400), txt = `ARCHIVISION · ${V.rec?.name || ''} · ${new Date().toLocaleString('fr-FR', {dateStyle:'short', timeStyle:'short'})}`;
  g.font = `600 ${Math.round(13 * s)}px Inter, sans-serif`; const tw = g.measureText(txt).width;
  g.fillStyle = 'rgba(11,31,69,.72)'; const pad = 8 * s; g.fillRect(12 * s, H - 12 * s - 26 * s, tw + pad * 2, 26 * s);
  g.fillStyle = '#fff'; g.fillText(txt, 12 * s + pad, H - 12 * s - 8 * s);
}
async function thumbnail(){
  const sel = V.sel; if (sel) select(null);
  const st = V.style; if (st !== 'mat'){ V.style = 'mat'; refreshMaterials(); }
  const p0 = camera.position.clone(), t0 = controls.target.clone(); setView('iso', true);
  const w0 = canvas.clientWidth || 640, h0 = canvas.clientHeight || 400;
  renderer.render(scene, camera);
  const W = 480, H = 300, out = document.createElement('canvas'); out.width = W; out.height = H; const g = out.getContext('2d');
  const sw = canvas.width, sh = canvas.height, k = Math.max(W / sw, H / sh);
  g.fillStyle = '#F2F6F9'; g.fillRect(0, 0, W, H);
  g.drawImage(canvas, (W - sw * k) / 2, (H - sh * k) / 2, sw * k, sh * k);
  camera.position.copy(p0); controls.target.copy(t0); controls.update();
  if (st !== 'mat'){ V.style = st; refreshMaterials(); } if (sel) select(sel);
  markDirty(); void w0; void h0;
  return out.toDataURL('image/jpeg', 0.8);
}
