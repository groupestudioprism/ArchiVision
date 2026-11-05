# AR en direct : « Voir en AR sur le PC »

Une maquette lancée en AR sur le téléphone (hôte) génère un **code de session**. Sur le PC, l'utilisateur saisit ce code dans ARCHIVISION
(bouton « Voir en AR sur le PC » : accueil ou barre du haut de la visite) : une interface permet de **déplacer uniquement** des éléments ;
les déplacements sont appliqués en direct sur la maquette AR du téléphone.

Le site web (WebXR, Android/Chrome) implémente déjà l'hôte (`src/engine/55-arlive.js`). L'app Unity mobile doit faire de même avec ce protocole.

## Transport : Firestore (projet `archivision-studioprism`)
Les deux côtés doivent être **connectés** à Firebase Auth (compte Studio Prism ou connexion anonyme à activer côté app Unity).

### `arLive/{code}` : session (créée par l'hôte)
| champ | type | rôle |
|---|---|---|
| `v` | int | version du protocole, `1` |
| `code` | string | identique à l'id du document |
| `owner` | string | `uid` Firebase de l'hôte |
| `expires` | number | fin de validité (ms epoch) ; `expiresAt` : même instant en Timestamp. Durée : 2 h, 3 h maximum (règles) |
| `createdAt` | Timestamp | `serverTimestamp()` |
| `state` | string | `open`, puis `closed` quand l'hôte quitte l'AR (seule modification autorisée) |
| `model` | string | nom de la maquette (80 car. max) |
| `objects` | array | éléments déplaçables, 300 max, les plus volumineux d'abord |

Élément : `{i, n, k, p:[x,y,z], s:[sx,sy,sz]}`
- `i` entier = index dans le tableau (identifiant de l'élément, stable pendant la session) ;
- `n` nom, `k` type (ex. « Mur ») ;
- `p` **centre de la boîte englobante** de l'élément, en mètres, dans le repère de la maquette (échelle 1:1, indépendant de l'échelle AR et du placement) ;
- `s` dimensions de la boîte (m), affichée côté PC comme proxy de l'élément.

**Repère** : celui de la maquette glTF (Y vers le haut, droitier). Dans Unity (gauche), appliquer `x → -x` à l'entrée comme à la sortie si l'import glTF utilisé le fait.

### `arLive/{code}/moves/{i}` : dernière position de l'élément `i` (écrite par le PC)
`{i:int, p:[x,y,z], seq:number, c:string, t:serverTimestamp}`
- `p` : **nouvelle position absolue du centre** de l'élément (même repère et même unité que `objects[i].p`). Le déplacement à appliquer est `p - objects[i].p`, appliqué à la position de l'élément d'origine (translation seule : pas de rotation, d'échelle ni de suppression, les règles l'interdisent) ;
- `seq` : compteur croissant par émetteur ; l'hôte ignore un message dont `seq` n'est pas supérieur au dernier appliqué pour cet `i` ;
- `c` : identifiant aléatoire du client émetteur (8 car.) pour ignorer ses propres échos ;
- ré-écrire `p` = position d'origine remet l'élément en place.

## Déroulé
1. **Hôte** : à l'entrée en AR, tire un code de 6 caractères parmi `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, écrit `arLive/{code}` (en cas d'erreur de permission, le code existe déjà : en tirer un autre), affiche « Code PC : ABC 123 » dans l'app, puis écoute `arLive/{code}/moves` (`addSnapshotListener`) et applique chaque `added`/`modified` dont `c` ≠ son propre id.
2. **PC** : saisit le code (casse et espaces ignorés), lit `arLive/{code}` (refus si absent, expiré ou `state == closed`), affiche les boîtes, écrit dans `moves/{i}` au plus toutes les 80 ms pendant le glissement et une dernière fois au relâchement. Il écoute aussi la session pour afficher « Session AR terminée ».
3. **Fin** : l'hôte passe `state` à `closed` et remet les éléments à leur place d'origine. Le code expire de toute façon à `expiresAt` (lecture et écriture refusées ensuite).

## Sécurité
Règles dans la source unique de Studio Prism (dépôt Studio-Prism, `firebase/rules/12-archivision.rules`, assemblé dans `firebase/firestore.rules`) : accès réservé aux comptes connectés ; lecture document par document (`get`, aucun `list` : on ne peut pas énumérer les codes) ; code de 6 caractères (32^6 ≈ 10^9 combinaisons) valable 2 h ; écriture limitée à `moves` avec champs et types contrôlés (position seule) ; seul l'hôte clôt ou supprime la session. Activer si possible la suppression automatique (TTL) sur `arLive.expiresAt`.

## Mode démo (tests)
Sans Firebase, le site utilise `BroadcastChannel` + `localStorage` : mêmes messages, entre deux onglets du même navigateur (`tests/arlive.py`).
