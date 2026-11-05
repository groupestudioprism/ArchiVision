# Remplacer la maison d'exemple par une maison faite dans ARCHIPLAN

État : ARCHIVISION utilise la maison construite par le code (`src/engine/35-sample.js`). Dès qu'un fichier `assets/maison-exemple.glb` existe, c'est lui qui est ouvert à la place (même bouton « Exemple », même identifiant `sample`, mêmes limites : visite et AR, pas de stockage). Sans ce fichier, rien ne change.

## Procédure (quand ARCHIPLAN a terminé ses modifications)
1. Dans ARCHIPLAN, dessiner ou ouvrir la maison, puis **Exporter → Archi Vision (.glb)** (ou le paquet `.zip` : le `.glb` est repéré automatiquement).
2. Vérifier : `python3 tools/maison-exemple.py EXPORT.glb` (GLB valide ? données ARCHIPLAN présentes ? taille ≤ 8 Mo ?).
3. Installer : `python3 tools/maison-exemple.py EXPORT.glb --install` → écrit `assets/maison-exemple.glb` et `assets/maison-exemple.json`.
4. Tester : `python3 -m http.server 8810`, ouvrir `dist/archivision-demo.html`, « Exemple » ; `python3 tests/e2e.py`.
5. Enregistrer (commit) et pousser sur `main` : GitHub Pages met le site à jour.

Retour à la maison du code : `python3 tools/maison-exemple.py --remove`.

Le nom affiché est celui du projet ARCHIPLAN (données `archiplan.projet` du GLB), sinon « Maison d'exemple ».
