# ARCHIVISION · application Android (Unity)

Application ARCHIVISION de Studio Prism pour téléphone et tablette Android : maquettes BIM en visite 3D et en réalité augmentée, avec le compte Studio Prism, la licence et le stockage intégrés.

## Contenu

| Dossier | Rôle |
|---|---|
| `Assets/ArchiVision/Runtime/Core` | configuration, formats français, erreurs, HTTP |
| `Runtime/Services` | compte Studio Prism (Firebase REST : connexion, inscription, fiche client, PIN, licence), stockage chiffré, bibliothèque locale |
| `Runtime/Viewer` | chargement GLB / glTF (glTFast) avec données BIM, vue 3D, caméra (orbite, marche, 360°), maison d'exemple |
| `Runtime/AR` | réalité augmentée ARCore (AR Foundation) : surfaces, réticule, pose, échelle 1:1 à 1:200, hauteur, rotation, gestes, inspection, capture, lumière estimée, occlusion |
| `Runtime/UI` | interface UI Toolkit (écrans, panneau BIM, AR, galerie) |
| `Runtime/Native` + `Plugins/Android` | sélecteur de fichiers, galerie du téléphone, partage, biométrie, conversion IFC → GLB (WebView + `ifc-convert.html` du site) |
| `Editor` | `AVSetup` (menu **ArchiVision** : configurer, passer en Android, construire la scène, construire l'APK) et le pont de pilotage `AVBridge` |

## Construire

1. Unity 6.6 (6000.6.6f1) avec le module Android (SDK/NDK/JDK).
2. Menu **ArchiVision → 1. Configurer le projet**, **0. Passer la cible en Android**, **2. Construire la scène**, **3. Construire l'APK**.
3. L'APK est écrit dans `Builds/ArchiVision.apk`.

Aucun mot de passe ni clé secrète dans le code : la configuration Firebase est publique par conception (la sécurité repose sur les règles Firestore).
