/* ============ Maquette d'exemple : petite maison (structure et données au format de l'export ARCHIPLAN) ============ */
function buildSample(){
  const M = (nom, color, o = {}) => { const m = new THREE.MeshStandardMaterial({color, roughness:o.r ?? 0.85, metalness:o.m ?? 0, transparent:!!o.t, opacity:o.t ?? 1, depthWrite:!o.t, side:o.t ? THREE.DoubleSide : THREE.FrontSide}); m.name = nom; return m; };
  const mat = {
    enduit:M('Enduit ciment', 0xEDE6DA), beton:M('Béton armé C30/37', 0xB9B9B4), parquet:M('Parquet chêne', 0xB98B5E, {r:0.6}), tuile:M('Tuile terre cuite', 0xA6533A, {r:0.75}),
    bois:M('Bois peint', 0x3E4A57, {r:0.5}), alu:M('Aluminium laqué', 0x2B3440, {r:0.4, m:0.4}), verre:M('Vitrage', 0x9FC9E0, {r:0.05, t:0.32}), platre:M('Plaque de plâtre BA13', 0xF4F3EF), terrain:M('Gazon', 0x7C9A5A, {r:1}),
  };
  const root = new THREE.Group(); root.name = 'Maison d\'exemple';
  root.userData.archiplan = {version:'exemple', projet:'Maison d\'exemple', unites:'m', axeHaut:'Y', niveaux:[{n:'RDC', z:0}]};
  const lv = new THREE.Group(); lv.name = 'RDC'; lv.userData = {niveau:'RDC', altitude:0}; root.add(lv);
  let id = 1;
  const el = (name, info, parts) => {
    const g = new THREE.Group(); g.name = `${name} ${id}`; g.userData = {archiplan:{id:id++, ...info}};
    for (const [m, x0, y0, z0, x1, y1, z1] of parts){ const geo = new THREE.BoxGeometry(x1 - x0, y1 - y0, z1 - z0); geo.translate((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2); const me = new THREE.Mesh(geo, m); me.name = `${g.name} - ${m.name}`; me.userData = {materiau:m.name}; g.add(me); }
    lv.add(g); return g;
  };
  const W = 10, D = 7, H = 2.7, T = 0.3;
  const murExt = {type:'wall', composition:'Parpaing 20 + doublage laine minérale 100 + BA13', couches:[{materiau:'Enduit ciment', epaisseur:0.015}, {materiau:'Bloc béton creux', epaisseur:0.2}, {materiau:'Laine minérale', epaisseur:0.1}, {materiau:'Plaque de plâtre BA13', epaisseur:0.013}], U:0.31, lot:'02 Gros œuvre'};
  el('Dalle', {type:'slab', composition:'Dallage béton 12 cm sur isolant', couches:[{materiau:'Parquet chêne', epaisseur:0.015}, {materiau:'Béton armé C30/37', epaisseur:0.12}, {materiau:'Polystyrène extrudé', epaisseur:0.1}], U:0.27, lot:'02 Gros œuvre'},
    [[mat.beton, -0.1, -0.25, -0.1, W + 0.1, -0.015, D + 0.1], [mat.parquet, T, -0.015, T, W - T, 0, D - T]]);
  // façade sud (z = D) : porte + baie ; nord (z = 0) : 2 fenêtres ; est / ouest : 1 fenêtre
  const wallX = (z0, holes) => { const parts = []; let x = 0; for (const [a, b, s, h] of holes){ parts.push([mat.enduit, x, 0, z0, a, H, z0 + T]); if (s > 0) parts.push([mat.enduit, a, 0, z0, b, s, z0 + T]); parts.push([mat.enduit, a, h, z0, b, H, z0 + T]); x = b; } parts.push([mat.enduit, x, 0, z0, W, H, z0 + T]); return parts; };
  const wallZ = (x0, holes) => { const parts = []; let z = T; for (const [a, b, s, h] of holes){ parts.push([mat.enduit, x0, 0, z, x0 + T, H, a]); if (s > 0) parts.push([mat.enduit, x0, 0, a, x0 + T, s, b]); parts.push([mat.enduit, x0, h, a, x0 + T, H, b]); z = b; } parts.push([mat.enduit, x0, 0, z, x0 + T, H, D - T]); return parts; };
  el('Mur', {...murExt, longueur:W}, wallX(D - T, [[1.6, 2.5, 0, 2.15], [4.4, 7.4, 0, 2.15]]));
  el('Mur', {...murExt, longueur:W}, wallX(0, [[1.8, 3.0, 1.0, 2.15], [6.6, 7.8, 1.0, 2.15]]));
  el('Mur', {...murExt, longueur:D}, wallZ(0, [[2.9, 4.1, 1.0, 2.15]]));
  el('Mur', {...murExt, longueur:D}, wallZ(W - T, [[2.9, 4.1, 1.0, 2.15]]));
  el('Cloison', {type:'wall', composition:'Cloison 72/48', couches:[{materiau:'Plaque de plâtre BA13', epaisseur:0.013}, {materiau:'Laine minérale', epaisseur:0.045}, {materiau:'Plaque de plâtre BA13', epaisseur:0.013}], U:0.62, lot:'05 Plâtrerie'},
    [[mat.platre, 6.2, 0, T, 6.27, H, 3.2], [mat.platre, 6.2, 2.15, 3.2, 6.27, H, 4.1], [mat.platre, 6.2, 0, 4.1, 6.27, H, D - T]]);
  const win = (name, x0, y0, z0, x1, y1, z1, fam, dims) => {
    const th = 0.06, fr = 0.06, horiz = x1 - x0 > z1 - z0, parts = [];
    if (horiz){ const zc = (z0 + z1) / 2; parts.push([mat.alu, x0, y0, zc - th, x1, y0 + fr, zc + th], [mat.alu, x0, y1 - fr, zc - th, x1, y1, zc + th], [mat.alu, x0, y0, zc - th, x0 + fr, y1, zc + th], [mat.alu, x1 - fr, y0, zc - th, x1, y1, zc + th], [mat.verre, x0 + fr, y0 + fr, zc - 0.01, x1 - fr, y1 - fr, zc + 0.01]); }
    else { const xc = (x0 + x1) / 2; parts.push([mat.alu, xc - th, y0, z0, xc + th, y0 + fr, z1], [mat.alu, xc - th, y1 - fr, z0, xc + th, y1, z1], [mat.alu, xc - th, y0, z0, xc + th, y1, z0 + fr], [mat.alu, xc - th, y0, z1 - fr, xc + th, y1, z1], [mat.verre, xc - 0.01, y0 + fr, z0 + fr, xc + 0.01, y1 - fr, z1 - fr]); }
    el(name, {type:'inst', famille:fam, categorie:'Menuiseries', typeFamille:dims, parametres:{largeur:Math.round((horiz ? x1 - x0 : z1 - z0) * 100) / 100, hauteur:Math.round((y1 - y0) * 100) / 100}, Uw:1.3}, parts);
  };
  win('Baie coulissante', 4.4, 0, D - T, 7.4, 2.15, D, 'Baie coulissante alu', '300×215');
  win('Fenêtre', 1.8, 1.0, 0, 3.0, 2.15, T, 'Fenêtre 2 vantaux', '120×115'); win('Fenêtre', 6.6, 1.0, 0, 7.8, 2.15, T, 'Fenêtre 2 vantaux', '120×115');
  win('Fenêtre', 0, 1.0, 2.9, T, 2.15, 4.1, 'Fenêtre 2 vantaux', '120×115'); win('Fenêtre', W - T, 1.0, 2.9, W, 2.15, 4.1, 'Fenêtre 2 vantaux', '120×115');
  el('Porte d\'entrée', {type:'inst', famille:'Porte d\'entrée bois', categorie:'Menuiseries', typeFamille:'90×215', parametres:{largeur:0.9, hauteur:2.15}, Ud:1.4}, [[mat.bois, 1.6, 0, D - T + 0.1, 2.5, 2.15, D - T + 0.16]]);
  // toiture à deux pans (faîtage est-ouest)
  const roof = new THREE.Group(); roof.name = 'Toiture ' + id; roof.userData = {archiplan:{id:id++, type:'roof', composition:'Charpente bois, laine 300, tuiles', couches:[{materiau:'Tuile terre cuite', epaisseur:0.02}, {materiau:'Laine minérale', epaisseur:0.3}, {materiau:'Plaque de plâtre BA13', epaisseur:0.013}], U:0.14, lot:'03 Charpente couverture', pente:'35°'}};
  const ov = 0.4, rise = Math.tan(THREE.MathUtils.degToRad(35)) * (D / 2 + ov), len = Math.hypot(D / 2 + ov, rise);
  for (const s of [-1, 1]){ const geo = new THREE.BoxGeometry(W + 2 * ov, 0.12, len); const me = new THREE.Mesh(geo, mat.tuile); me.name = 'Pan de toiture - Tuile terre cuite';
    me.position.set(W / 2, H + rise / 2 + 0.05, D / 2 + s * (D / 2 + ov) / 2); me.rotation.x = s * Math.atan2(rise, D / 2 + ov); roof.add(me); }
  const gab = new THREE.Shape([new THREE.Vector2(0, 0), new THREE.Vector2(D, 0), new THREE.Vector2(D / 2, rise * D / 2 / (D / 2 + ov))]);
  for (const x of [0, W - T]){ const g = new THREE.ExtrudeGeometry(gab, {depth:T, bevelEnabled:false}); g.rotateY(-Math.PI / 2); g.translate(x + T, H, 0); const me = new THREE.Mesh(g, mat.enduit); me.name = 'Pignon - Enduit ciment'; roof.add(me); }
  lv.add(roof);
  el('Table', {type:'inst', famille:'Table de séjour', categorie:'Mobilier', typeFamille:'180×90'}, [[mat.bois, 2.2, 0.72, 2.0, 4.0, 0.76, 2.9], [mat.bois, 2.3, 0, 2.1, 2.36, 0.72, 2.16], [mat.bois, 3.84, 0, 2.1, 3.9, 0.72, 2.16], [mat.bois, 2.3, 0, 2.74, 2.36, 0.72, 2.8], [mat.bois, 3.84, 0, 2.74, 3.9, 0.72, 2.8]]);
  el('Plan de travail', {type:'inst', famille:'Cuisine linéaire', categorie:'Mobilier', typeFamille:'300×65'}, [[mat.platre, T, 0, 0.35, T + 3.0, 0.9, 0.95], [mat.beton, T, 0.9, 0.33, T + 3.0, 0.94, 0.97]]);
  const site = new THREE.Group(); site.name = 'Terrain'; site.userData = {archiplan:{type:'terrain', source:'Maquette d\'exemple'}};
  const t = new THREE.Mesh(new THREE.BoxGeometry(W + 8, 0.1, D + 8), mat.terrain); t.position.set(W / 2, -0.3, D / 2); t.name = 'Terrain - Gazon'; site.add(t); root.add(site);
  root.traverse(o => { if (o.isMesh){ o.castShadow = o.receiveShadow = true; } });
  return root;
}
