# ARCHIVISION · Studio Prism

Maquettes BIM en **visite 3D** et en **réalité augmentée**, sur téléphone, tablette et ordinateur. Version web de l'app Android ArchiVision (Unity / ARCore), avec la direction artistique d'**ARCHIPLAN** et d'**ARCHITRAFIC** (même en-tête, rail d'outils, panneau, accueil, thème clair / sombre). Une app du pôle **Prism App** de Studio Prism.

**En ligne (après activation de GitHub Pages) :** https://groupestudioprism.github.io/ArchiVision/

> **État :** testé dans Chromium sans interface (`tests/e2e.py` : 17 contrôles, ordinateur et téléphone simulé), avec un GLB et un IFC exportés par ARCHIPLAN. **La réalité augmentée WebXR n'a pas pu être testée ici** (pas de caméra ni d'ARCore dans l'environnement de test) : à valider sur un téléphone Android avec Chrome, et AR Quick Look sur iPhone.

## Ce que reprend l'APK

| APK ArchiVision (Unity) | Web ARCHIVISION |
| --- | --- |
| Connexion Studio Prism (Firebase REST), mot de passe oublié, changement de mot de passe | Même compte et même module qu'ARCHIPLAN / ARCHITRAFIC : connexion, Google, mot de passe oublié, fiche client, code PIN + biométrie, licence |
| Onglet **FICHIERS** : maquettes GLB / IFC, sélection, suppression | Accueil **Mes maquettes** : import GLB, glTF (+ .bin, textures), **IFC** (converti sur l'appareil, sans serveur), **ZIP** d'ARCHIPLAN ; renommer, télécharger, supprimer |
| Onglet **ARCHIVISION** (AR) : viser une surface, PLACER, RESET, CAPTURE, INSPECTER, échelle 1:100 → 1:1, hauteur ± | Bouton **Voir en AR** (WebXR) : mêmes boutons, curseur d'échelle (1:200 → 1:1, préréglages 1:100, 1:50, 1:20, 1:1), hauteur ±5 cm, rotation ±15°, pincer / tourner à deux doigts |
| Onglet **GALERIE** : captures, annotation, enregistrer dans le téléphone, supprimer | Onglet **Galerie** (accueil et panneau) : captures annotées, partage / téléchargement (l'annotation est inscrite sous l'image), suppression |
| Onglet **VISITE** : ORBITE, MARCHE, 360°, RECENTRER | Rail d'outils : **Orbite**, **Marche** (joystick ou ZQSD, suit les sols), **Immersion 360°**, **Recentrer** ; en plus **Inspecter**, **Mesure**, **Coupe**, styles Matériaux / Ombré / Filaire / Rayons X |
| Hiérarchie BIM (Tout afficher), **IFC OBJECT DATA** | Onglet **Maquette** (arborescence, masquer / isoler / centrer, filtre) et onglet **Données** (classe IFC, nom, GlobalId, couches de matériaux, Psets : résistance au feu, porteur…, données ARCHIPLAN : composition, U, lot, famille) |

## Réalité augmentée

- **Android** : Chrome + ARCore (« Services Google Play pour la RA »). Session WebXR `immersive-ar` avec détection des surfaces (`hit-test`) et interface superposée (`dom-overlay`). Toucher l'image pose aussi la maquette.
- **Capture en AR** : si le navigateur fournit l'image de la caméra (option `camera-access`), la photo contient la caméra + la maquette ; sinon, la maquette seule sur fond bleu (c'est signalé à l'écran).
- **iPhone / iPad** : Safari n'a pas WebXR ; ARCHIVISION convertit la maquette en **USDZ** et l'ouvre dans **AR Quick Look** (menu Fichier → Maquette pour iPhone, ou bouton Voir en AR).
- **Ordinateur** : une fenêtre explique comment passer sur téléphone ; la visite 3D reste disponible.

## Stockage

Les maquettes (GLB, IFC d'origine + GLB converti) et les photos sont gardées **sur l'appareil** (IndexedDB), rangées par compte Studio Prism. Rien n'est envoyé dans le cloud. Bouton « Protéger le stockage » pour demander au navigateur de ne pas les effacer. Les unités sont détectées (mm, cm, m) et modifiables dans l'onglet Infos.

## Licence

Produit `archivision` du site Studio Prism : un administrateur attribue la licence `licenses/{uid}_archivision` (Administration → Utilisateurs → fiche du compte → « + Ajouter une licence » → ARCHIVISION), ou l'utilisateur active une clé. Sans licence active : **mode découverte** (maquette d'exemple, visite 3D, hiérarchie et données BIM) ; l'**import** des maquettes, la **réalité augmentée** et les **exports** demandent la licence. Un administrateur a tous les droits. Hors ligne, les derniers droits vérifiés restent valables 7 jours. (`FREE_FOR_ALL` dans `src/js/auth.js` ouvre tout gratuitement si on le passe à `true`.)

## Contenu du dépôt

| Fichier | Rôle |
| --- | --- |
| `src/parts/` | Structure de la page : intro, compte, visite, interface AR, accueil |
| `src/css/` | Charte Studio Prism d'ARCHIPLAN (`app.css`, `home.css`, `auth.css`), intro (`splash.css`), couleurs ARCHIVISION (`brand.css`), visite et AR (`vision.css`) |
| `src/js/auth.js`, `auth-end.js` | Compte Studio Prism (même module qu'ARCHIPLAN / ARCHITRAFIC, produit `archivision`) |
| `src/js/intro.js` | Animation d'ouverture (maquette qui se construit, balayage AR, emblème officiel) ; `?intro=1` la rejoue, une fois par session sinon ; bouton « Passer l'animation » |
| `src/js/vision-home.js` | Accueil « Mes maquettes », galerie et visionneuse de photos, stockage IndexedDB |
| `src/engine/` | Moteur (module ES, Three.js r160) : vue et styles, navigation, chargement GLB / glTF / IFC (web-ifc 0.0.77) / ZIP, maquette d'exemple, BIM, réalité augmentée, interface |
| `assets/` | Logo ARCHIVISION (Drive ARCHIVISION / 02 Logo), icônes de l'app |
| `tools/build.py` | `python3 tools/build.py` → `index.html` (en ligne, Firebase branché), `dist/archivision-demo.html` (démo autonome), `manifest.webmanifest` |
| `tests/e2e.py` | Test de bout en bout (Playwright) ; `tests/fixtures/` : GLB et IFC exportés par ARCHIPLAN |

On modifie `src/`, on lance `python3 tools/build.py`, on enregistre (commit) sur `main` : GitHub Pages met le site à jour.

**Tester :** `python3 -m http.server 8810` à la racine, puis `python3 tests/e2e.py`.

La configuration Firebase écrite dans `index.html` est publique par conception ; la sécurité repose sur les règles Firestore du site. **Ne jamais ajouter de clé privée ni de compte de service dans ce dépôt.** Le domaine `groupestudioprism.github.io` doit figurer dans les domaines autorisés de Firebase Authentication (déjà le cas pour ARCHIPLAN et ARCHITRAFIC).

## Limites connues

- Réalité augmentée non testée sur un vrai téléphone (voir l'état en tête).
- Pas de synchronisation entre appareils : une maquette importée sur l'ordinateur doit être réimportée sur le téléphone.
- IFC lourds (plus de ~100 Mo) : la conversion se fait dans le navigateur et peut être lente sur téléphone.
- Les messages « expected REF » de web-ifc sur les IFC d'ARCHIPLAN (OwnerHistory vide, autorisé en IFC4) sont masqués ; la maquette se lit correctement.
