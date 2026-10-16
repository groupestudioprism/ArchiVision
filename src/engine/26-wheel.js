/* Zoom molette fluide (vue orbite) : même pas par cran que le zoom d'origine d'OrbitControls (×0,95 par cran),
   mais appliqué en glissement sur ≈ 150 ms au lieu d'un saut. Les modes visite et panorama gardent leur propre molette. */
(() => {
  const STEP = 0.95, v = new THREE.Vector3(); let pend = 0, raf = 0, last = 0;
  const tick = now => { const dt = Math.min(50, now - (last || now)); last = now;
    if (Math.abs(pend) < 1e-4 || !controls.enabled){ pend = 0; raf = 0; return; }
    const part = Math.abs(pend) < 2e-3 ? pend : pend * (1 - Math.exp(-dt / 55));   /* fin du glissement : on applique le reste, le pas par cran est exact */ pend -= part;
    const dist = camera.position.distanceTo(controls.target), nd = Math.max(controls.minDistance, Math.min(controls.maxDistance, dist * Math.exp(part)));
    v.subVectors(camera.position, controls.target).setLength(nd); camera.position.copy(controls.target).add(v); controls.update();
    raf = requestAnimationFrame(tick); };
  canvas.addEventListener('wheel', ev => {
    if (V.tool === 'pano' || V.tool === 'walk' || !controls.enabled || !controls.enableZoom || camera.isOrthographicCamera) return;
    ev.preventDefault(); ev.stopImmediatePropagation();
    let d = ev.deltaY * (ev.deltaMode === 1 ? 33 : ev.deltaMode === 2 ? 400 : 1); if (!d || !isFinite(d)) return; if (ev.ctrlKey) d *= 2.5;
    const n = Math.max(-1.5, Math.min(1.5, d / 100));
    pend = Math.max(-1.2, Math.min(1.2, pend - n * Math.log(STEP)));   // molette vers le haut (n < 0) : rapprocher (facteur < 1)
    if (!raf){ last = 0; raf = requestAnimationFrame(tick); }
  }, {capture:true, passive:false});
})();
