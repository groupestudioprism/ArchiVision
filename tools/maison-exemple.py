#!/usr/bin/env python3
"""Convertit un export ARCHIPLAN (Archi Vision .glb, ou paquet .zip contenant un .glb) en maison d'exemple d'ARCHIVISION.

  python3 tools/maison-exemple.py EXPORT.glb|EXPORT.zip            # vérifie seulement (rien n'est écrit)
  python3 tools/maison-exemple.py EXPORT.glb --install             # écrit assets/maison-exemple.glb (+ .json) : remplace la maison d'exemple
  python3 tools/maison-exemple.py EXPORT.zip --install --out DIR   # écrit dans un autre dossier (essais)
  python3 tools/maison-exemple.py --remove                         # supprime assets/maison-exemple.* : retour à la maison intégrée au code

Tant que assets/maison-exemple.glb n'existe pas, ARCHIVISION garde la maison construite par le code (src/engine/35-sample.js).
Voir docs/MAISON_EXEMPLE.md pour la procédure complète. Aucune dépendance (bibliothèque standard seulement)."""
import argparse, json, pathlib, struct, sys, zipfile
ROOT = pathlib.Path(__file__).resolve().parent.parent
MAX = 8 * 1024 * 1024          # au-delà, la première ouverture de l'exemple devient lente sur téléphone
def read_glb(path):
    p = pathlib.Path(path)
    if not p.is_file(): sys.exit(f'Fichier introuvable : {p}')
    if p.suffix.lower() == '.zip':
        with zipfile.ZipFile(p) as z:
            glbs = [n for n in z.namelist() if n.lower().endswith('.glb') and not n.startswith('__MACOSX')]
            if not glbs: sys.exit('Le .zip ne contient aucun .glb (exportez « Archi Vision (.glb) » ou le paquet complet depuis ARCHIPLAN).')
            glbs.sort(key=lambda n: ('archivision' not in n.lower(), len(n)))
            print('GLB retenu dans le zip :', glbs[0]); return z.read(glbs[0])
    return p.read_bytes()
def inspect(data):
    if len(data) < 20 or data[:4] != b'glTF': sys.exit('Ce fichier n\'est pas un GLB (en-tête « glTF » absent).')
    ver, total = struct.unpack('<II', data[4:12])
    if ver != 2 or total > len(data): sys.exit('GLB incomplet ou version non prise en charge (attendu : glTF 2.0).')
    clen, ctype = struct.unpack('<II', data[12:20])
    if ctype != 0x4E4F534A: sys.exit('Premier bloc du GLB illisible (JSON attendu).')
    j = json.loads(data[20:20 + clen].decode('utf-8'))
    nodes, meshes = j.get('nodes', []), j.get('meshes', [])
    if not meshes: sys.exit('Le GLB ne contient aucune géométrie.')
    ap = None
    for o in [j.get('scenes', [{}])[0].get('extras')] + [n.get('extras') for n in nodes]:
        if isinstance(o, dict) and 'archiplan' in o: ap = o['archiplan']; break
    info = {'taille': len(data), 'noeuds': len(nodes), 'maillages': len(meshes), 'materiaux': len(j.get('materials', [])), 'archiplan': bool(ap)}
    if ap and isinstance(ap, dict): info['projet'] = ap.get('projet')
    return info
def main():
    a = argparse.ArgumentParser(description=__doc__.split('\n')[0]); a.add_argument('export', nargs='?'); a.add_argument('--install', action='store_true'); a.add_argument('--remove', action='store_true'); a.add_argument('--out', default=str(ROOT / 'assets'))
    o = a.parse_args(); out = pathlib.Path(o.out)
    if o.remove:
        for n in ('maison-exemple.glb', 'maison-exemple.json'): (out / n).unlink(missing_ok=True)
        print('Maison d\'exemple du code rétablie (fichiers supprimés de', out, ')'); return
    if not o.export: a.error('indiquez l\'export ARCHIPLAN (.glb ou .zip)')
    data = read_glb(o.export); info = inspect(data)
    print('GLB valide :', json.dumps(info, ensure_ascii=False))
    if not info['archiplan']: print('ATTENTION : pas de données ARCHIPLAN dans ce GLB (hiérarchie, compositions, U…) : l\'onglet Données sera pauvre. Réexportez depuis ARCHIPLAN.')
    if info['taille'] > MAX: print(f'ATTENTION : {info["taille"] / 1e6:.1f} Mo (> {MAX // 2**20} Mo) : allégez le projet ARCHIPLAN.')
    if not o.install: print('Vérification seulement : ajoutez --install pour remplacer la maison d\'exemple.'); return
    out.mkdir(parents=True, exist_ok=True)
    (out / 'maison-exemple.glb').write_bytes(data)
    (out / 'maison-exemple.json').write_text(json.dumps({'nom': info.get('projet') or 'Maison d\'exemple', **info}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Écrit :', out / 'maison-exemple.glb', '— à enregistrer (commit) puis pousser ; GitHub Pages met à jour le site.')
if __name__ == '__main__': main()
