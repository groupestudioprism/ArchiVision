/* ============ Réalité augmentée (WebXR « immersive-ar ») : détection des surfaces, pose, échelle, hauteur, rotation, inspection, capture ============
   Android : Chrome + ARCore (« Services Google Play pour la RA »). iPhone / iPad : Safari ne propose pas WebXR → AR Quick Look (USDZ).
   L'interface (#arUi) est superposée à la caméra par l'option « dom-overlay ». */
const AR = {supported:null, session:null, hitSource:null, ref:null, placed:false, scale:1, height:0, rot:0, baseYaw:0, base:new THREE.Vector3(), hitOk:false, lastHitT:0, t0:0,
  save:null, capture:null, gl:null, binding:null, camAccess:false};
(async () => { try { AR.supported = !!(navigator.xr && await navigator.xr.isSessionSupported('immersive-ar')); } catch(e){ AR.supported = false; } updateArUi(); })();

const reticle = new THREE.Group(); reticle.matrixAutoUpdate = false; reticle.visible = false; scene.add(reticle);
{
  const ring = new THREE.Mesh(new THREE.RingGeometry(0.09, 0.12, 40).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({color:0x2FD3B4, transparent:true, opacity:0.95, depthTest:false}));
  const dot = new THREE.Mesh(new THREE.CircleGeometry(0.018, 20).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({color:0xffffff, depthTest:false}));
  ring.renderOrder = dot.renderOrder = 20; reticle.add(ring, dot);
}
const arStatus = t => { const s = $('#arStatus'); if (s && s.textContent !== t) s.textContent = t; };
const ratioLabel = s => s >= 0.999 ? '1:1' : '1:' + Math.round(1 / s);
const S_MIN = 0.005, slider2scale = v => Math.pow(10, Math.log10(S_MIN) + v / 1000 * -Math.log10(S_MIN)), scale2slider = s => Math.round((Math.log10(s) - Math.log10(S_MIN)) / -Math.log10(S_MIN) * 1000);
function updateArUi(){
  const ok = AR.supported;
  $('#arInfo').textContent = AR.supported == null ? 'Vérification de l\'appareil…' : ok ? `Pose sur le sol ou une table · échelle ${ratioLabel(AR.scale)}` : isIOS ? 'iPhone / iPad : AR Quick Look' : /Android/i.test(navigator.userAgent) ? 'Non pris en charge sur cet appareil' : 'À lancer sur téléphone ou tablette';
  $('#arScaleSel').value = String(AR.scale);
}
function setArScale(s, from){
  AR.scale = clamp(s, S_MIN, 1); pivot.scale.setScalar(AR.scale);
  $('#arScaleV').textContent = ratioLabel(AR.scale); if (from !== 'slider') $('#arScale').value = scale2slider(AR.scale);
  const sel = $('#arScaleSel'); if ([...sel.options].some(o => +o.value === AR.scale)) sel.value = String(AR.scale);
  if (AR.session) applyPlacement(); updateArUi();
}
function applyPlacement(){
  pivot.position.copy(AR.base); pivot.position.y += AR.height;
  pivot.rotation.set(0, AR.baseYaw + THREE.MathUtils.degToRad(AR.rot), 0); pivot.scale.setScalar(AR.scale);
  $('#arHV').textContent = `${AR.height >= 0 ? '+' : '−'}${nf(Math.abs(AR.height), 2)} m`;
  // le soleil suit la maquette pour garder des ombres nettes
  const r = V.radius * AR.scale; sun.position.copy(pivot.position).add(new THREE.Vector3(r * 1.2, r * 2.2 + 0.5, r * 0.9)); sun.target.position.copy(pivot.position);
  const sc = sun.shadow.camera; sc.left = sc.bottom = -r * 1.5; sc.right = sc.top = r * 1.5; sc.far = r * 8 + 2; sc.updateProjectionMatrix();
}

async function startAR(){
  if (!V.root) return toast('Aucune maquette chargée. Choisissez un fichier dans l\'accueil.');
  if (needLicence()) return;
  if (AR.session) return;
  if (!AR.supported) return arFallback();
  let session;
  try {
    session = await navigator.xr.requestSession('immersive-ar', {requiredFeatures:['hit-test'], optionalFeatures:['dom-overlay', 'local-floor', 'camera-access', 'light-estimation'], domOverlay:{root:$('#arUi')}});
  } catch(e){ console.warn(e); return e?.name === 'NotAllowedError' || e?.name === 'SecurityError' ? toast('Accès à la caméra refusé. Autorisez-le dans les réglages du téléphone.') : arFallback(true); }
  AR.session = session; AR.placed = false; AR.height = 0; AR.rot = 0; AR.hitOk = false; AR.t0 = performance.now();
  AR.save = {cam:camera.position.clone(), tgt:controls.target.clone(), tool:V.tool, style:V.style, sec:renderer.clippingPlanes.slice()};
  if (V.tool === 'walk' || V.tool === 'pano') setTool('orbit');
  V.mode = 'ar'; document.body.dataset.ar = '1';
  renderer.clippingPlanes = []; select(null); clearMeasures();
  if (V.style !== 'mat') setStyle('mat');
  scene.background = null; renderer.setClearColor(0x000000, 0); if (grid) grid.visible = false;
  pivot.add(shadowPlane); pivot.visible = false; shadowPlane.material.opacity = 0.32;
  setArScale(+$('#arScaleSel').value || AR.scale, 'init');
  const ui = $('#arUi'); ui.hidden = false; $('#arCard').hidden = true; $('#arPlace').textContent = 'PLACER';
  arStatus('Activation de la caméra...');
  session.addEventListener('end', endAR);
  session.addEventListener('select', () => { if (!AR.placed) placeHere(); });
  renderer.xr.setReferenceSpaceType('local');
  try { await renderer.xr.setSession(session); } catch(e){ toast('Réalité augmentée non prise en charge par cet appareil.'); session.end().catch(() => {}); return; }
  AR.ref = renderer.xr.getReferenceSpace();
  try { const viewer = await session.requestReferenceSpace('viewer'); AR.hitSource = await session.requestHitTestSource({space:viewer}); }
  catch(e){ toast('Détection des surfaces indisponible sur cet appareil.'); }
  AR.camAccess = !!session.enabledFeatures?.includes?.('camera-access');
  AR.gl = renderer.getContext(); try { AR.binding = AR.camAccess ? new XRWebGLBinding(session, AR.gl) : null; } catch(e){ AR.binding = null; }
  setModeButtons();
}
function endAR(){
  const s = AR.save || {}; AR.session = null; AR.hitSource = null; AR.placed = false; reticle.visible = false;
  V.mode = 'tour'; delete document.body.dataset.ar; $('#arUi').hidden = true;
  ground.add(shadowPlane); pivot.visible = true; pivot.position.set(0, 0, 0); pivot.rotation.set(0, 0, 0); pivot.scale.setScalar(1);
  renderer.setClearColor(0x000000, 0); applyTheme(); if (grid) grid.visible = true; normalizeModel(V.unitScale);
  if (s.sec?.length) renderer.clippingPlanes = s.sec;
  setTimeout(() => { resize(); if (s.cam){ camera.position.copy(s.cam); controls.target.copy(s.tgt); controls.update(); } setTool(s.tool && s.tool !== 'walk' && s.tool !== 'pano' ? s.tool : 'orbit'); if (s.style && s.style !== 'mat') setStyle(s.style); markDirty(); }, 60);
  select(null); setModeButtons();
}
function placeHere(){
  if (!reticle.visible){ arStatus('Aucune surface détectée. Déplacez lentement le téléphone vers le sol ou une table.'); return; }
  AR.base.setFromMatrixPosition(reticle.matrix);
  const cam = renderer.xr.getCamera(), cp = new THREE.Vector3().setFromMatrixPosition(cam.matrixWorld);
  AR.baseYaw = Math.atan2(cp.x - AR.base.x, cp.z - AR.base.z);   // la façade « avant » (+z) regarde l'utilisateur
  AR.placed = true; pivot.visible = true; applyPlacement();
  $('#arPlace').textContent = 'DÉPLACER ICI'; arStatus(`Maquette placée · ${ratioLabel(AR.scale)}`);
  navigator.vibrate?.(15);
}
function resetAR(){ AR.placed = false; AR.height = 0; AR.rot = 0; pivot.visible = false; select(null); $('#arCard').hidden = true; $('#arPlace').textContent = 'PLACER'; arStatus('Visez une surface plane pour placer la maquette'); }
function inspectAR(){
  if (!AR.placed) return arStatus('Placez d\'abord la maquette pour l\'inspecter.');
  const cam = renderer.xr.getCamera(), o = new THREE.Vector3().setFromMatrixPosition(cam.matrixWorld), d = new THREE.Vector3(0, 0, -1).transformDirection(cam.matrixWorld);
  ray.set(o, d); ray.far = 200; const hit = pickRay(ray); ray.far = Infinity;
  if (!hit){ $('#arCard').hidden = true; select(null); return arStatus('Visez un élément de la maquette puis appuyez sur INSPECTER.'); }
  const el = elementOf(hit.object); select(el, {from:'ar'});
  const card = $('#arCard'); card.innerHTML = shortInfo(el) + '<button class="ar-x small" data-arcard="close" aria-label="Fermer">✕</button>'; card.hidden = false;
  arStatus(`Élément : ${objLabel(el)}`);
}

/* boucle XR : cible sur les surfaces, messages d'aide, capture */
tickers.add((dt, now, frame) => {
  if (!AR.session || !frame) return;
  if (AR.hitSource){
    const res = frame.getHitTestResults(AR.hitSource);
    if (res.length){ const pose = res[0].getPose(AR.ref); if (pose){ reticle.matrix.fromArray(pose.transform.matrix); reticle.matrixWorldNeedsUpdate = true; reticle.visible = true; AR.hitOk = true; AR.lastHitT = now; } }
    else if (now - AR.lastHitT > 400) reticle.visible = false;
  }
  if (!AR.placed){
    if (reticle.visible) arStatus('Visez une surface plane puis appuyez sur PLACER');
    else arStatus(now - AR.t0 > 6000 ? 'Aucune surface détectée. Déplacez lentement le téléphone vers le sol ou une table.' : 'Visez une surface plane pour placer la maquette');
  }
  if (AR.capture){ const c = AR.capture; AR.capture = null; arCaptureFrame(frame).then(c.res, c.rej); }
});

/* capture en AR : image de la caméra (si le navigateur la donne : « camera-access ») + maquette */
function captureAR(){ return new Promise((res, rej) => { AR.capture = {res, rej}; }); }
async function arCaptureFrame(frame){
  const gl = AR.gl, pose = frame.getViewerPose(AR.ref), view = pose?.views?.[0]; if (!view) throw new Error('vue AR indisponible');
  const cam = renderer.xr.getCamera(), sub = cam.cameras?.[0] || cam;
  const bl = AR.session.renderState.baseLayer, W = bl?.framebufferWidth || canvas.width, H = bl?.framebufferHeight || canvas.height;
  const out = document.createElement('canvas'); out.width = W; out.height = H; const g = out.getContext('2d');
  let cameraOk = false;
  if (AR.binding && view.camera){
    try {
      const tex = AR.binding.getCameraImage(view.camera), cw = view.camera.width, ch = view.camera.height;
      const fb = gl.createFramebuffer(), prev = gl.getParameter(gl.FRAMEBUFFER_BINDING);
      gl.bindFramebuffer(gl.FRAMEBUFFER, fb); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
      const px = new Uint8Array(cw * ch * 4); gl.readPixels(0, 0, cw, ch, gl.RGBA, gl.UNSIGNED_BYTE, px);
      gl.bindFramebuffer(gl.FRAMEBUFFER, prev); gl.deleteFramebuffer(fb);
      const ci = new ImageData(new Uint8ClampedArray(px.buffer), cw, ch), tmp = document.createElement('canvas'); tmp.width = cw; tmp.height = ch; tmp.getContext('2d').putImageData(ci, 0, 0);
      g.save(); g.translate(0, H); g.scale(1, -1); g.drawImage(tmp, 0, 0, W, H); g.restore(); cameraOk = true;
    } catch(e){ console.warn('camera-access', e); }
  }
  if (!cameraOk){ const gr = g.createLinearGradient(0, 0, 0, H); gr.addColorStop(0, '#14365F'); gr.addColorStop(1, '#0B1F45'); g.fillStyle = gr; g.fillRect(0, 0, W, H); }
  // maquette : rendu hors XR avec la caméra du téléphone
  const rt = new THREE.WebGLRenderTarget(W, H, {samples:4}); rt.texture.colorSpace = THREE.SRGBColorSpace;
  const pc = new THREE.PerspectiveCamera(); pc.projectionMatrix.copy(sub.projectionMatrix); pc.projectionMatrixInverse.copy(sub.projectionMatrixInverse);
  pc.matrixWorld.copy(sub.matrixWorld); pc.matrixWorldInverse.copy(sub.matrixWorldInverse); pc.matrixAutoUpdate = false; pc.matrixWorldAutoUpdate = false;
  const rv = reticle.visible; reticle.visible = false;
  renderer.xr.enabled = false; const prevRT = renderer.getRenderTarget();
  renderer.setRenderTarget(rt); renderer.setClearColor(0x000000, 0); renderer.clear(); renderer.render(scene, pc); renderer.setRenderTarget(prevRT);
  renderer.xr.enabled = true; reticle.visible = rv;
  const px = new Uint8Array(W * H * 4); renderer.readRenderTargetPixels(rt, 0, 0, W, H, px); rt.dispose();
  const tmp = document.createElement('canvas'); tmp.width = W; tmp.height = H; tmp.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(px.buffer), W, H), 0, 0);
  g.save(); g.translate(0, H); g.scale(1, -1); g.drawImage(tmp, 0, 0); g.restore();
  drawStamp(g, W, H);
  const blob = await new Promise(r => out.toBlob(r, 'image/jpeg', 0.9));
  return {blob, w:W, h:H, canvas:out, cameraOk};
}

/* ---------- Sans WebXR : iPhone (AR Quick Look), autres appareils ---------- */
async function makeUSDZ(){
  const {USDZExporter} = await import('three/addons/exporters/USDZExporter.js');
  const st = V.style, sel = V.sel; if (st !== 'mat') setStyle('mat'); if (sel) select(null);
  const edges = []; V.root.traverse(o => { if (o.userData.__edges){ edges.push(o); o.remove(o.userData.__edges); } });
  try { const data = await new USDZExporter().parse(V.root, {quickLookCompatible:true}); return new Blob([data], {type:'model/vnd.usdz+zip'}); }
  finally { edges.forEach(o => o.add(o.userData.__edges)); if (st !== 'mat') setStyle(st); if (sel) select(sel); }
}
async function openQuickLook(){
  window.AVUI?.busy('Préparation pour AR Quick Look…');
  try {
    const blob = await makeUSDZ(); window.AVUI?.busy();
    const a = document.createElement('a'); a.rel = 'ar'; a.href = URL.createObjectURL(blob) + '#allowsContentScaling=1'; a.appendChild(document.createElement('img'));
    document.body.appendChild(a); a.click(); setTimeout(() => a.remove(), 30000);
  } catch(e){ window.AVUI?.busy(); console.error(e); toast('AR Quick Look : la conversion a échoué.'); }
}
function arFallback(failed){
  const ui = window.AVUI; if (!ui) return;
  const android = /Android/i.test(navigator.userAgent);
  const body = isIOS ? `<p class="dlg-p">Safari ne propose pas encore la réalité augmentée WebXR sur iPhone et iPad. ARCHIVISION ouvre la maquette dans <b>AR Quick Look</b>, la réalité augmentée d'Apple : posez-la sur le sol, déplacez-la et redimensionnez-la avec deux doigts.</p>`
    : android ? `<p class="dlg-p">${failed ? 'La session de réalité augmentée n\'a pas pu démarrer.' : 'Réalité augmentée non prise en charge par cet appareil.'}</p><ol class="dlg-ol"><li>Ouvrez ARCHIVISION dans <b>Chrome</b>.</li><li>Installez ou mettez à jour <b>Services Google Play pour la RA</b> (ARCore) depuis le Play Store.</li><li>Autorisez l'accès à la caméra, puis réessayez.</li></ol>`
    : `<p class="dlg-p">La réalité augmentée se lance sur un <b>téléphone ou une tablette</b> : Android avec Chrome (ARCore), ou iPhone / iPad avec AR Quick Look.</p><ol class="dlg-ol"><li>Ouvrez ARCHIVISION sur votre téléphone, avec le même compte Studio Prism.</li><li>Importez-y la maquette (les maquettes sont gardées sur l'appareil où elles ont été importées).</li><li>Touchez <b>Voir en AR</b>.</li></ol><p class="hint">Sur ordinateur, la visite 3D (orbite, marche, 360°) reste disponible.</p>`;
  ui.dlg(`<h3>Réalité augmentée</h3>${body}<div class="dlg-acts"><button type="button" class="ap-btn alt" data-dlg="close">Fermer</button>${isIOS ? '<button type="button" class="ap-btn" id="dlgQL">Ouvrir dans AR Quick Look</button>' : ''}</div>`, d => {
    const b = d.querySelector('#dlgQL'); if (b) b.onclick = () => { ui.closeDlg(); openQuickLook(); };
  });
}

/* ---------- Interface AR ---------- */
$('#arPlace').addEventListener('click', placeHere);
$('#arReset').addEventListener('click', resetAR);
$('#arInspect').addEventListener('click', inspectAR);
$('#arExit').addEventListener('click', () => AR.session?.end());
$('#arCap').addEventListener('click', async () => {
  try { const shot = await captureAR(); await savePhoto(shot, 'ar'); arStatus(shot.cameraOk ? 'Capture enregistrée dans la galerie ArchiVision.' : 'Capture de la maquette seule (image caméra non fournie par le navigateur).'); }
  catch(e){ console.error(e); arStatus('ÉCHEC - Échec de la capture : ' + (e?.message || '')); }
});
$('#arScale').addEventListener('input', e => setArScale(slider2scale(+e.target.value), 'slider'));
$('#arUi').addEventListener('click', e => {
  const s = e.target.closest('[data-ars]'); if (s) return setArScale(+s.dataset.ars);
  const h = e.target.closest('[data-arh]'); if (h){ AR.height = Math.round((AR.height + +h.dataset.arh) * 100) / 100; return applyPlacement(); }
  const r = e.target.closest('[data-arr]'); if (r){ AR.rot += +r.dataset.arr; return applyPlacement(); }
  if (e.target.closest('[data-arcard]')){ $('#arCard').hidden = true; select(null); }
});
// les touches sur les boutons ne déclenchent pas la pose (« select » WebXR)
$$('#arUi .ar-top, #arUi .ar-adjust, #arUi .ar-bottom, #arUi .ar-card').forEach(el => el.addEventListener('beforexrselect', e => e.preventDefault()));
// gestes à deux doigts sur l'image : pincer = échelle, tourner = rotation
(() => {
  const pts = new Map(); let g0 = null;
  const ui = $('#arUi'), two = () => [...pts.values()];
  ui.addEventListener('pointerdown', e => { if (e.target !== ui) return; pts.set(e.pointerId, {x:e.clientX, y:e.clientY}); if (pts.size === 2){ const [a, b] = two(); g0 = {d:Math.hypot(a.x - b.x, a.y - b.y), ang:Math.atan2(b.y - a.y, b.x - a.x), s:AR.scale, r:AR.rot}; } });
  ui.addEventListener('pointermove', e => { if (!pts.has(e.pointerId)) return; pts.set(e.pointerId, {x:e.clientX, y:e.clientY}); if (pts.size === 2 && g0 && AR.placed){ const [a, b] = two(); const d = Math.hypot(a.x - b.x, a.y - b.y), ang = Math.atan2(b.y - a.y, b.x - a.x); setArScale(g0.s * d / g0.d); AR.rot = g0.r - THREE.MathUtils.radToDeg(ang - g0.ang); applyPlacement(); } });
  const up = e => { pts.delete(e.pointerId); if (pts.size < 2) g0 = null; };
  ui.addEventListener('pointerup', up); ui.addEventListener('pointercancel', up);
})();
$('#arScaleSel').addEventListener('change', e => setArScale(+e.target.value));
$('#arGo').addEventListener('click', () => startAR());
