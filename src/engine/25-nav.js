/* ============ Navigation : orbite, marche (première personne), immersion 360°, inspection, mesure, coupe ============ */
const EYE = 1.6, WALK_SPEED = 1.4;
const look = {yaw:0, pitch:0};
const keys = new Set();
let joyVec = {x:0, y:0};
const TOOL_HINT = {
  orbit:'<b>Orbite</b> : glissez pour tourner, pincez ou molette pour zoomer, deux doigts ou clic droit pour déplacer. Touchez un élément pour voir ses données.',
  walk:'<b>Marche</b> : glissez pour regarder, joystick ou <kbd>Z</kbd><kbd>Q</kbd><kbd>S</kbd><kbd>D</kbd> / flèches pour avancer. La caméra suit les sols et les escaliers.',
  pano:'<b>Immersion 360°</b> : glissez pour regarder autour de vous, pincez pour zoomer. Touchez deux fois le sol pour vous y déplacer.',
  inspect:'<b>Inspecter</b> : touchez un élément de la maquette pour afficher ses données BIM.',
  measure:'<b>Mesure</b> : touchez deux points de la maquette. <kbd>Échap</kbd> efface les mesures.',
  section:'<b>Coupe</b> : réglez la hauteur du plan de coupe avec le curseur.',
};
function setTool(t){
  if (V.mode === 'ar') return;
  const prev = V.tool; V.tool = t;
  $$('.tools [data-tool]').forEach(b => b.setAttribute('aria-pressed', b.dataset.tool === t));
  prompt(TOOL_HINT[t] || '');
  const fp = t === 'walk' || t === 'pano';
  controls.enabled = !fp;
  $('#joy').hidden = t !== 'walk';
  if (fp && !(prev === 'walk' || prev === 'pano')) enterFirstPerson(t);
  if (!fp && (prev === 'walk' || prev === 'pano')) exitFirstPerson();
  if (t === 'pano'){ camera.fov = 75; camera.updateProjectionMatrix(); }
  else if (camera.fov !== 50){ camera.fov = 50; camera.updateProjectionMatrix(); }
  $('#secBar').hidden = t !== 'section' && !renderer.clippingPlanes.length;
  if (t === 'section' && !renderer.clippingPlanes.length) setSection(0.62);
  vpEl.dataset.tool = t; markDirty();
}
function enterFirstPerson(t){
  // on se place dans la maquette, à hauteur d'yeux, face au centre
  const tgt = controls.target.clone(); tgt.y = 0;
  const inside = V.box.containsPoint(camera.position) && camera.position.y < V.size.y;
  if (!inside){
    const dir = camera.position.clone().sub(tgt); dir.y = 0; if (dir.lengthSq() < 1e-6) dir.set(0, 0, 1); dir.normalize();
    const edge = Math.max(V.size.x, V.size.z) / 2;
    const p = t === 'walk' ? dir.multiplyScalar(edge + 3) : new THREE.Vector3(0, 0, 0);
    p.y = floorAt(p) + EYE; camera.position.copy(p);
  }
  const fwd = new THREE.Vector3(0, camera.position.y, 0).sub(camera.position); fwd.y = 0;
  if (fwd.lengthSq() < 1e-4) fwd.set(0, 0, -1);
  look.yaw = Math.atan2(-fwd.x, -fwd.z); look.pitch = 0; applyLook();
  toast(t === 'walk' ? 'Marche : la caméra est à hauteur d\'yeux (1,60 m)' : 'Immersion 360° : regardez autour de vous');
}
function exitFirstPerson(){
  const d = new THREE.Vector3(0, 0, -1).applyQuaternion(camera.quaternion);
  controls.target.copy(camera.position).add(d.multiplyScalar(Math.max(2, V.radius / 2))); controls.update();
}
function applyLook(){ camera.rotation.set(look.pitch, look.yaw, 0, 'YXZ'); markDirty(); }
const downRay = new THREE.Raycaster(); downRay.layers.enableAll();   // sols fusionnés (calque 1) compris
/* altitude du sol sous un point : premier élément visible sous les yeux (sols, escaliers), sinon 0 */
function floorAt(p, from = p.y + 0.6){
  if (!V.meshes.length) return 0;
  downRay.set(new THREE.Vector3(p.x, from, p.z), new THREE.Vector3(0, -1, 0)); downRay.far = 50;
  const h = downRay.intersectObjects(V.meshes.filter(isShown), false)[0];
  return h ? h.point.y : 0;
}
let floorT = 0;
tickers.add((dt, now) => {
  if (V.tool !== 'walk' || V.mode === 'ar') return;
  const f = (keys.has('z') || keys.has('w') || keys.has('arrowup') ? 1 : 0) - (keys.has('s') || keys.has('arrowdown') ? 1 : 0) + (-joyVec.y);
  const s = (keys.has('d') || keys.has('arrowright') ? 1 : 0) - (keys.has('q') || keys.has('a') || keys.has('arrowleft') ? 1 : 0) + joyVec.x;
  if (Math.abs(f) < 0.05 && Math.abs(s) < 0.05) return;
  const run = keys.has('shift') ? 2.5 : 1, v = WALK_SPEED * run * dt;
  const fw = new THREE.Vector3(-Math.sin(look.yaw), 0, -Math.cos(look.yaw)), rt = new THREE.Vector3(Math.cos(look.yaw), 0, -Math.sin(look.yaw));
  camera.position.addScaledVector(fw, f * v).addScaledVector(rt, s * v);
  if (now - floorT > 90){ floorT = now; const fy = floorAt(camera.position, camera.position.y - EYE + 0.45); const want = fy + EYE; camera.position.y += (want - camera.position.y) * 0.5; }
  markDirty();
});

/* pointeur : glisser pour regarder (marche, 360°), toucher pour sélectionner ou mesurer */
const ptr = {id:null, x:0, y:0, x0:0, y0:0, moved:false, t:0, n:0, lastTap:0, pinch:null};
const touches = new Map();
canvas.addEventListener('pointerdown', e => {
  touches.set(e.pointerId, {x:e.clientX, y:e.clientY});
  if (touches.size === 2 && (V.tool === 'pano' || V.tool === 'walk')){ const [a, b] = [...touches.values()]; ptr.pinch = {d:Math.hypot(a.x - b.x, a.y - b.y), fov:camera.fov}; }
  if (ptr.id !== null) return;
  ptr.id = e.pointerId; ptr.x = ptr.x0 = e.clientX; ptr.y = ptr.y0 = e.clientY; ptr.moved = false; ptr.t = performance.now();
  if (V.tool === 'walk' || V.tool === 'pano') canvas.setPointerCapture(e.pointerId);
});
canvas.addEventListener('pointermove', e => {
  if (touches.has(e.pointerId)) touches.set(e.pointerId, {x:e.clientX, y:e.clientY});
  if (ptr.pinch && touches.size === 2){ const [a, b] = [...touches.values()]; const d = Math.hypot(a.x - b.x, a.y - b.y); camera.fov = clamp(ptr.pinch.fov * ptr.pinch.d / d, 25, 100); camera.updateProjectionMatrix(); markDirty(); return; }
  if (e.pointerId !== ptr.id) return;
  const dx = e.clientX - ptr.x, dy = e.clientY - ptr.y; ptr.x = e.clientX; ptr.y = e.clientY;
  if (Math.hypot(e.clientX - ptr.x0, e.clientY - ptr.y0) > 6) ptr.moved = true;
  if (V.tool === 'walk' || V.tool === 'pano'){
    const k = (camera.fov / 50) * 0.0042;
    look.yaw += dx * k; look.pitch = clamp(look.pitch + dy * k, -1.45, 1.45); applyLook();
  }
});
function endPtr(e){
  touches.delete(e.pointerId); if (touches.size < 2) ptr.pinch = null;
  if (e.pointerId !== ptr.id) return; ptr.id = null;
  if (ptr.moved || performance.now() - ptr.t > 600 || e.type === 'pointercancel') return;
  onTap(e.clientX, e.clientY);
}
canvas.addEventListener('pointerup', endPtr); canvas.addEventListener('pointercancel', endPtr);
canvas.addEventListener('wheel', e => { if (V.tool !== 'pano' && V.tool !== 'walk') return; e.preventDefault(); camera.fov = clamp(camera.fov * (1 + Math.sign(e.deltaY) * 0.08), 25, 100); camera.updateProjectionMatrix(); markDirty(); }, {passive:false});
function onTap(x, y){
  const hit = pickAt(x, y), now = performance.now(), dbl = now - ptr.lastTap < 320; ptr.lastTap = now;
  if (V.tool === 'measure'){ if (hit) addMeasurePoint(hit.point); return; }
  if (V.tool === 'pano' && dbl && hit){ moveTo(hit.point, hit.face); return; }
  if (V.tool === 'walk' && dbl && hit){ moveTo(hit.point, hit.face); return; }
  if (hit){ select(elementOf(hit.object)); if (V.tool === 'inspect' || isNarrow.matches) openPanelTab('props'); }
  else select(null);
}
function moveTo(p, face){
  const n = face?.normal?.clone().transformDirection(V.root.matrixWorld);
  const q = p.clone();
  if (n && n.y > 0.6) q.y += EYE; else { const back = camera.position.clone().sub(p).setY(0).normalize().multiplyScalar(0.6); q.add(back); q.y = floorAt(q) + EYE; }
  const from = camera.position.clone(), t0 = performance.now();
  const f = (dt, now) => { const k = clamp((now - t0) / 380, 0, 1); camera.position.lerpVectors(from, q, k * (2 - k)); markDirty(); if (k >= 1) tickers.delete(f); };
  tickers.add(f);
}
/* joystick tactile (marche) */
(() => {
  const j = $('#joy'), knob = j.querySelector('i'); let id = null, cx = 0, cy = 0;
  j.addEventListener('pointerdown', e => { id = e.pointerId; j.setPointerCapture(id); const r = j.getBoundingClientRect(); cx = r.left + r.width / 2; cy = r.top + r.height / 2; move(e); });
  const move = e => { if (e.pointerId !== id) return; const R = j.clientWidth / 2; let dx = e.clientX - cx, dy = e.clientY - cy; const d = Math.hypot(dx, dy); if (d > R){ dx *= R / d; dy *= R / d; } knob.style.transform = `translate(${dx}px,${dy}px)`; joyVec = {x:dx / R, y:dy / R}; };
  const end = e => { if (e.pointerId !== id) return; id = null; knob.style.transform = ''; joyVec = {x:0, y:0}; };
  j.addEventListener('pointermove', move); j.addEventListener('pointerup', end); j.addEventListener('pointercancel', end);
})();

/* ---------- Mesures ---------- */
const measureGroup = new THREE.Group(); measureGroup.renderOrder = 10; scene.add(measureGroup);
const measures = []; let mPending = null;
const mDot = new THREE.MeshBasicMaterial({color:0x2FD3B4, depthTest:false}), mLine = new THREE.LineBasicMaterial({color:0x2FD3B4, depthTest:false});
function addMeasurePoint(p){
  const s = new THREE.Mesh(new THREE.SphereGeometry(Math.max(0.025, V.radius / 300), 12, 8), mDot); s.position.copy(p); s.renderOrder = 10; measureGroup.add(s);
  if (!mPending){ mPending = {a:p.clone(), dots:[s]}; prompt('<b>Mesure</b> : touchez le second point.'); markDirty(); return; }
  const a = mPending.a, b = p.clone(), d = a.distanceTo(b);
  const l = new THREE.Line(new THREE.BufferGeometry().setFromPoints([a, b]), mLine); l.renderOrder = 10; measureGroup.add(l);
  const lab = document.createElement('div'); lab.className = 'dim sel'; lab.textContent = fmtM(d / 1); $('#labels').append(lab);
  measures.push({a, b, d, objs:[...mPending.dots, s, l], lab}); mPending = null;
  const dy = Math.abs(b.y - a.y), dh = Math.hypot(b.x - a.x, b.z - a.z);
  prompt(`<b>Mesure</b> : ${fmtM(d)} <span class="mut">· horizontale ${fmtM(dh)} · verticale ${fmtM(dy)}</span> — touchez deux autres points, ou <kbd>Échap</kbd> pour effacer.`);
  markDirty();
}
function clearMeasures(){
  for (const m of measures){ m.objs.forEach(o => { measureGroup.remove(o); o.geometry?.dispose(); }); m.lab.remove(); }
  measures.length = 0; if (mPending){ mPending.dots.forEach(o => measureGroup.remove(o)); mPending = null; } markDirty();
}
const _p = new THREE.Vector3();
function placeLabels(){
  if (!measures.length) return;
  const r = canvas.getBoundingClientRect();
  for (const m of measures){
    _p.copy(m.a).add(m.b).multiplyScalar(0.5).project(camera);
    const vis = _p.z < 1 && Math.abs(_p.x) < 1.2 && Math.abs(_p.y) < 1.2;
    m.lab.style.display = vis ? '' : 'none';
    if (vis) m.lab.style.transform = `translate(${(_p.x + 1) / 2 * r.width}px,${(1 - _p.y) / 2 * r.height}px) translate(-50%,-50%)`;
  }
}
function drawMeasuresOn(g, k){
  const r = canvas.getBoundingClientRect();
  for (const m of measures){
    _p.copy(m.a).add(m.b).multiplyScalar(0.5).project(camera); if (_p.z >= 1) continue;
    const x = (_p.x + 1) / 2 * r.width * k, y = (1 - _p.y) / 2 * r.height * k, t = fmtM(m.d);
    g.font = `700 ${Math.round(13 * k)}px IBM Plex Mono, monospace`; const w = g.measureText(t).width + 12 * k;
    g.fillStyle = '#0B1F45'; g.fillRect(x - w / 2, y - 11 * k, w, 22 * k); g.fillStyle = '#7FE0CF'; g.fillText(t, x - w / 2 + 6 * k, y + 5 * k);
  }
}

/* ---------- Coupe horizontale ---------- */
const secPlane = new THREE.Plane(new THREE.Vector3(0, -1, 0), 0);
function setSection(k){
  if (k == null){ renderer.clippingPlanes = []; $('#secBar').hidden = V.tool !== 'section'; markDirty(); return; }
  const y = k * V.size.y; secPlane.constant = y + 1e-4; renderer.clippingPlanes = [secPlane];
  $('#secH').value = k; $('#secV').textContent = `${nf(y, 2)} m`; $('#secBar').hidden = false; markDirty();
}
$('#secH').addEventListener('input', e => setSection(+e.target.value));
$('#secOff').addEventListener('click', () => { setSection(null); if (V.tool === 'section') setTool('orbit'); });

/* clavier */
window.addEventListener('keydown', e => {
  if (e.target.closest?.('input,select,textarea') || !$('#home').hidden || V.mode === 'ar') return;
  const k = e.key.toLowerCase(); keys.add(k);
  if (V.tool === 'walk' && ['z', 'q', 's', 'd', 'w', 'a', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright'].includes(k)){ e.preventDefault(); return; }
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  const map = {o:'orbit', w:'walk', i:'pano', s:'inspect', m:'measure', x:'section'};
  if (map[k]){ setTool(map[k]); e.preventDefault(); }
  else if (k === 'f'){ setView('iso'); }
  else if (k === 'c'){ doCapture(); }
  else if (k === 'escape'){ if (measures.length || mPending) clearMeasures(); else if (V.tool !== 'orbit') setTool('orbit'); else select(null); }
});
window.addEventListener('keyup', e => keys.delete(e.key.toLowerCase()));
window.addEventListener('blur', () => keys.clear());
