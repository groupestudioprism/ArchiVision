/* ============ Pilote du convertisseur (application Android ARCHIVISION) ============
   La WebView invisible de l'app expose « AVIfcBridge » : size(), read(i) (morceaux base64 de 192 Ko), write(b64), progress(msg), done(meta), fail(msg). */
(async () => {
  const B = window.AVIfcBridge, st = document.getElementById('st');
  const say = m => { if (st) st.textContent = m; try { B?.progress(m); } catch(e){} };
  if (!B){ say('Convertisseur IFC → GLB interne à l\'application ARCHIVISION (Android). Aucune action n\'est possible depuis ce navigateur.'); return; }
  try {
    const size = Number(B.size()), CH = 192 * 1024, n = Math.ceil(size / CH), bytes = new Uint8Array(size);
    if (!size) throw new Error('fichier IFC vide.');
    let off = 0;
    for (let i = 0; i < n; i++){
      const bin = atob(B.read(i)); for (let k = 0; k < bin.length; k++) bytes[off++] = bin.charCodeAt(k);
      if (i % 10 === 0) say(`Lecture du fichier IFC… ${Math.round(off / size * 100)} %`);
    }
    if (off !== size) throw new Error('lecture incomplète du fichier IFC.');
    const root = await parseIFC(bytes, say);
    say('Préparation de la maquette 3D…'); await frame();
    const blob = await exportGLB(root), glb = new Uint8Array(await blob.arrayBuffer());
    const OUT = 96 * 1024;
    for (let i = 0; i < glb.length; i += OUT){
      const part = glb.subarray(i, i + OUT); let s = ''; for (let k = 0; k < part.length; k += 8192) s += String.fromCharCode.apply(null, part.subarray(k, k + 8192));
      B.write(btoa(s));
      if ((i / OUT) % 20 === 0) say(`Enregistrement… ${Math.round(i / glb.length * 100)} %`);
    }
    B.done(JSON.stringify({projet:root.userData.ifcProjet || null, octets:glb.length}));
  } catch(e){ try { B.fail(e?.message || String(e)); } catch(_){} }
})();
