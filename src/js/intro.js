/* Intro ARCHIVISION : la maquette prend vie.
   Un bâtiment se trace sur la grille du plan (emprise, fondations, murs, planchers), se pose en maquette, puis une ligne de détection AR balaie : derrière elle l'emblème OFFICIEL (assets/emblem-320.webp) se révèle et ses
   points d'ancrage se posent un à un ; ARCHI / VISION s'écrivent, la ligne de signature apparaît. Dernière image = écran de chargement stable (emblème officiel intact, barre, message #spMsg).
   Animations Web Animations API / CSS : déterministes, pausables, seekables. Aucune boucle rAF, aucun son.
   Contrat avec le reste de l'app : window.__spIntro.request() = « l'app peut fermer l'intro » ; la durée minimale (scène ≈ 3 s depuis le premier affichage) et la sortie (fondu 450 ms) sont gérées ici.
   ?intro=1 force le rejeu ; sinon une seule fois par session de navigation (sessionStorage 'prism-intro-archivision'). */
(function () {
  'use strict';
  var sp = document.getElementById('splash');
  if (!sp) return;
  var KEY = 'prism-intro-archivision', SCENE = 2900, HOLD_REDUCED = 600, HOLD_SEEN = 500, SKIP_HOLD = 250, EXIT = 450;
  var EIN = 'cubic-bezier(.22,.8,.2,1)', EIO = 'cubic-bezier(.65,0,.35,1)', EM = 'cubic-bezier(.4,0,.2,1)';
  var anims = [], closed = false, requested = false, tStart = 0, endAt = 0, timer = 0, mode = 'scene';
  var q = function (s) { return sp.querySelector(s); };

  var reduce = false, force = false, seen = false;
  try { reduce = matchMedia('(prefers-reduced-motion: reduce)').matches; } catch (e) {}
  try { force = /[?&]intro=1(&|$)/.test(location.search); } catch (e) {}
  try { seen = sessionStorage.getItem(KEY) === '1'; } catch (e) {}
  if (reduce) mode = 'reduced'; else if (seen && !force) mode = 'seen';

  function A(el, kf, delay, dur, ease, fill) {
    var a = el.animate(kf, { delay: delay, duration: dur, easing: ease || EIN, fill: fill || 'both' });
    anims.push(a); return a;
  }
  function el(tag, cls, svg) {
    var n = svg ? document.createElementNS('http://www.w3.org/2000/svg', tag) : document.createElement(tag);
    if (cls) n.setAttribute('class', cls); return n;
  }

  /* ---- clôture : fondu 450 ms puis retrait du DOM ---- */
  function schedule() {
    if (closed || !requested || !tStart) return;
    clearTimeout(timer);
    timer = setTimeout(finish, Math.max(0, endAt - performance.now()));
  }
  function finish() {
    if (closed) return; closed = true; clearTimeout(timer);
    sp.classList.add('done');
    document.removeEventListener('keydown', onKey, true);
    try { var a = document.activeElement; if (a && sp.contains(a)) a.blur(); } catch (e) {}
    setTimeout(function () { settle(); if (sp.parentNode) sp.remove(); }, EXIT + 30);
  }
  function settle() { // état final statique, sans animation en cours
    sp.classList.remove('sp-anim');
    anims.forEach(function (a) { try { a.cancel(); } catch (e) {} });
    try { sp.getAnimations({ subtree: true }).forEach(function (a) { if (a.effect && a.effect.target && !a.effect.target.closest('.sp-stage')) return; try { a.cancel(); } catch (e) {} }); } catch (e) {}
    anims = [];
    [].slice.call(sp.querySelectorAll('.sp-svg,.sp-lay,.sp-scan,.sp-dot')).forEach(function (n) { n.remove(); });
    var b = q('.sp-skip'); if (b) b.hidden = true;
  }
  function skip() {
    if (closed || mode !== 'scene' || skipped) return; skipped = true;
    settle();
    endAt = performance.now() + SKIP_HOLD; schedule();
  }
  var skipped = false;
  function onKey(e) { // tant que la scène joue, l'écran d'ouverture couvre tout : Échap et Entrée la passent
    if (closed || mode !== 'scene' || skipped) return;
    if (e.key === 'Escape' || e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); skip(); }
  }

  window.__spIntro = {
    request: function () { requested = true; schedule(); },
    skip: skip,
    get mode() { return mode; }
  };

  /* ---- départ de l'horloge : premier cadre affiché ---- */
  function begin() {
    if (tStart) return;
    tStart = performance.now();
    endAt = tStart + (mode === 'scene' ? SCENE : mode === 'reduced' ? HOLD_REDUCED : HOLD_SEEN);
    schedule();
    setTimeout(function () { // garde-fou : l'intro ne reste jamais indéfiniment (sauf message d'erreur de chargement)
      if (!closed && !q('.sp-msg.err')) { requested = true; endAt = 0; schedule(); }
    }, 16000);
  }
  try { requestAnimationFrame(function () { begin(); }); } catch (e) {}
  setTimeout(begin, 1500);

  if (mode === 'reduced') { sp.classList.add('sp-rm'); return; }
  if (mode === 'seen') { sp.classList.add('sp-seen'); return; }

  try { sessionStorage.setItem(KEY, '1'); } catch (e) {}

  /* ---- scène ---- */
  try {
    var fig = q('.sp-fig'), stage = q('.sp-stage');
    sp.classList.add('sp-anim');

    // projection isométrique du bâtiment (repère de l'emblème : 320 × 271)
    var S = 76, H = 1.1, M = H / 2, U = 1.5, V = 1;
    function P(u, v, z) { return [160 + (u - v) * 0.866 * S, 118 + (u + v) * 0.5 * S - z * S]; }
    function pts(a) { return a.map(function (p) { return p[0].toFixed(1) + ' ' + p[1].toFixed(1); }); }
    function len(a) { var L = 0; for (var i = 1; i < a.length; i++) L += Math.hypot(a[i][0] - a[i - 1][0], a[i][1] - a[i - 1][1]); return L; }

    var bld = el('svg', 'sp-svg', true); bld.setAttribute('viewBox', '0 0 320 271'); bld.setAttribute('aria-hidden', 'true');
    var hud = el('svg', 'sp-svg', true); hud.setAttribute('viewBox', '0 0 320 271'); hud.setAttribute('aria-hidden', 'true');
    var gG = el('g', '', true), gV = el('g', '', true); bld.appendChild(gG); bld.appendChild(gV);

    // trait : tracé progressif (stroke-dashoffset) ; chaque tracé suit exactement son chemin
    function line(parent, cls, a, delay, dur, ease, close) {
      var p = el('path', 'sp-l ' + cls, true), pa = close ? a.concat([a[0]]) : a, L = len(pa);
      p.setAttribute('d', 'M' + pts(pa).join('L'));
      p.style.strokeDasharray = L.toFixed(1); p.style.strokeDashoffset = L.toFixed(1);
      parent.appendChild(p);
      A(p, [{ strokeDashoffset: L.toFixed(1) }, { strokeDashoffset: 0 }], delay, dur, ease || EIO);
      return p;
    }
    function face(parent, cls, a, delay, dur, to) {
      var p = el('path', cls, true); p.setAttribute('d', 'M' + pts(a).join('L') + 'Z'); parent.appendChild(p);
      A(p, [{ opacity: 0 }, { opacity: to }], delay, dur, EIN); return p;
    }
    var b0 = P(0, 0, 0), bL = P(0, V, 0), bF = P(U, V, 0), bR = P(U, 0, 0);
    var t0 = P(0, 0, H), tL = P(0, V, H), tF = P(U, V, H), tR = P(U, 0, H);
    var m0 = P(0, 0, M), mL = P(0, V, M), mF = P(U, V, M), mR = P(U, 0, M);

    // 1. repères de construction qui prolongent l'emprise, puis emprise tracée au sol
    var ext = 0.28;
    [[bL, P(-ext, V, 0), P(0, V + ext, 0)], [bF, P(U + ext, V, 0), P(U, V + ext, 0)], [bR, P(U + ext, 0, 0), P(U, -ext, 0)], [b0, P(-ext, 0, 0), P(0, -ext, 0)]].forEach(function (c, i) {
      line(gG, 'cl', [c[1], c[0], c[2]], 140 + i * 50, 380, EIN);
    });
    face(gG, 'sp-fg', [bL, bF, bR, b0], 1300, 320, 0.13);
    line(gG, '', [bL, bF, bR], 200, 480, EIO);
    line(gG, 'h', [bR, b0, bL], 360, 380, EIO);
    // 2. fondations : dalle d'épaisseur 0,07
    var sl = 0.07, sL = P(0, V, -sl), sF = P(U, V, -sl), sR = P(U, 0, -sl);
    line(gV, '', [bL, sL, sF, sR, bR], 480, 360, EIO);
    line(gV, '', [bF, sF], 520, 200, EIO);
    // 3. murs : les arêtes montent depuis le sol
    line(gV, 'h', [b0, t0], 700, 420, EM);
    line(gV, '', [bL, tL], 640, 420, EM);
    line(gV, '', [bF, tF], 700, 420, EM);
    line(gV, '', [bR, tR], 760, 420, EM);
    // 4. plancher intermédiaire puis toiture
    line(gV, '', [mL, mF, mR], 900, 300, EIO); line(gV, 'h', [mR, m0, mL], 960, 260, EIO);
    line(gV, '', [tL, tF, tR], 1050, 340, EIO); line(gV, 'h', [tR, t0, tL], 1120, 280, EIO);
    // 5. cloisons du plan (au sol, couleur d'accent) et trames de façade
    line(gV, 'pt', [P(0.6, 0, 0), P(0.6, 0.55, 0), P(U, 0.55, 0)], 1000, 360, EIO);
    line(gV, 'pt', [P(0.6, 0.55, 0), P(0.6, V, 0)], 1100, 220, EIO);
    [0.5, 1.0].forEach(function (u, i) { line(gV, 'rib', [P(u, V, 0), P(u, V, H)], 1150 + i * 70, 300, EIO); });
    [1 / 3, 2 / 3].forEach(function (v, i) { line(gV, 'rib', [P(U, v, 0), P(U, v, H)], 1220 + i * 70, 300, EIO); });
    // 6. la maquette se pose : faces teintées
    face(gV, 'sp-fl', [bL, bF, tF, tL], 1300, 320, 0.14);
    face(gV, 'sp-fr', [bF, bR, tR, tF], 1340, 320, 0.17);
    face(gV, 'sp-ft', [tL, tF, tR, t0], 1380, 320, 0.20);

    // repères d'angle « verrouillés » autour de l'emblème
    var gH = el('g', '', true), arm = 26, o = -14, X1 = 320 - o, Y1 = 271 - o;
    [['M' + o + ' ' + (o + arm) + 'V' + o + 'H' + (o + arm)], ['M' + (X1 - arm) + ' ' + o + 'H' + X1 + 'V' + (o + arm)], ['M' + X1 + ' ' + (Y1 - arm) + 'V' + Y1 + 'H' + (X1 - arm)], ['M' + (o + arm) + ' ' + Y1 + 'H' + o + 'V' + (Y1 - arm)]].forEach(function (d) {
      var p = el('path', 'sp-br', true); p.setAttribute('d', d[0]); gH.appendChild(p);
      A(p, [{ opacity: 0, transform: 'scale(1.16)' }, { opacity: 1, transform: 'scale(1)' }], 1300, 360, EIN);
      A(p, [{ opacity: 1 }, { opacity: 0 }], 2520, 300, EM, 'forwards');
    });
    hud.appendChild(gH);

    // calques HTML : emblème officiel sans points d'ancrage (révélé par le balayage) ; points d'ancrage posés un à un
    var lay = el('img', 'sp-lay'); lay.src = 'assets/emblem-320-sans-points.webp'; lay.alt = ''; lay.setAttribute('aria-hidden', 'true'); lay.decoding = 'async';
    var scan = el('i', 'sp-scan'); scan.setAttribute('aria-hidden', 'true');
    var SW = 1500, SD = 620;
    A(lay, [{ clipPath: 'inset(0 100% 0 0)' }, { clipPath: 'inset(0 0 0 0)' }], SW, SD, EIO);
    A(bld, [{ clipPath: 'inset(-60% -60% -60% 0%)' }, { clipPath: 'inset(-60% -60% -60% 100%)' }], SW, SD, EIO);
    A(scan, [{ transform: 'translateX(-100%)', opacity: 0 }, { opacity: 1, offset: .08 }, { opacity: 1, offset: .88 }, { transform: 'translateX(0)', opacity: 0 }], SW - 40, SD + 40, EIO);

    fig.insertBefore(bld, fig.firstChild);
    fig.insertBefore(lay, q('.sp-mark'));
    fig.appendChild(scan); fig.appendChild(hud);

    var dots = [[6.5, 137.5], [6.5, 257.5], [186.2, 7], [219, 264.5], [281.5, 30.5], [294, 246], [311.7, 70]];
    dots.forEach(function (d, i) {
      var w = el('span', 'sp-dot'), c = el('i'), r = el('b'), t = 1750 + i * 85;
      w.style.left = (d[0] / 320 * 100) + '%'; w.style.top = (d[1] / 271 * 100) + '%';
      c.style.backgroundPosition = ((d[0] - 8.5) / 303 * 100) + '% ' + ((d[1] - 8.5) / 254 * 100) + '%';
      w.appendChild(c); w.appendChild(r); w.setAttribute('aria-hidden', 'true'); fig.appendChild(w);
      A(c, [{ opacity: 0, transform: 'scale(.15)' }, { opacity: 1, transform: 'scale(1)' }], t, 260, EIN);
      A(c, [{ opacity: 1 }, { opacity: 0 }], 2500, 1, 'steps(1,end)', 'forwards'); // relais par l'emblème intact, au même cadre
      A(r, [{ opacity: .9, transform: 'scale(.6)' }, { opacity: 0, transform: 'scale(2.6)' }], t + 40, 460, EIN, 'forwards');
    });
    // l'emblème officiel intact prend le relais (même image, même position) : état final strictement identique
    A(q('.sp-mark'), [{ visibility: 'hidden' }, { visibility: 'visible' }], 2500, 1, 'steps(1,end)');
    A(lay, [{ opacity: 1 }, { opacity: 0 }], 2500, 1, 'steps(1,end)', 'forwards');

    // environnement, mot, signature
    A(q('.sp-grid'), [{ opacity: 0 }, { opacity: 1 }], 50, 700, EIN);
    var w1 = q('.sp-word b'), w2 = q('.sp-word b.t');
    A(w1, [{ opacity: 1, clipPath: 'inset(0 100% 0 0)' }, { opacity: 1, clipPath: 'inset(0 0% 0 0)' }], 1800, 480, EIO);
    A(w2, [{ opacity: 1, clipPath: 'inset(0 100% 0 0)' }, { opacity: 1, clipPath: 'inset(0 0% 0 0)' }], 2060, 480, EIO);
    A(q('.sp-tag'), [{ opacity: 0, transform: 'translateY(6px)' }, { opacity: 1, transform: 'none' }], 2350, 380, EIN);
    A(q('.sp-road i'), [{ width: '0%' }, { width: '100%' }], 300, 2500, EM);

    // bouton « Passer l'animation »
    var sb = q('.sp-skip'); sb.hidden = false; sb.addEventListener('click', skip);
    try { sb.focus({ preventScroll: true }); } catch (e) {}
    document.addEventListener('keydown', onKey, true);
    anims[anims.length - 1].finished.then(function () { if (!closed && !skipped) sb.hidden = true; }, function () {});
  } catch (err) { // la scène ne doit jamais bloquer : on retombe sur l'état final statique
    try { console.warn('intro ARCHIVISION : ' + err); } catch (e) {}
    settle();
  }
})();
