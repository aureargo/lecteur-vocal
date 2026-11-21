# Moteur vocal (SAPI)

Fichier : [src/MoteurVocal.cs](../src/MoteurVocal.cs).

## Pourquoi SAPI en COM « dynamic »

| Option | Problème |
|---|---|
| `System.Speech` (.NET) | Ne voit que les voix inscrites en dur dans le registre (Hortense…). **Ne voit pas Denise/Henri**, fournies dynamiquement par NaturalVoiceSAPIAdapter |
| **SAPI 5 via COM** (`SAPI.SpVoice`) | ✅ Seul SAPI énumère les voix de l'adaptateur |

L'objet est créé par `Type.GetTypeFromProgID("SAPI.SpVoice")` et piloté en `dynamic` : pas de fichier d'interop, appels résolus à l'exécution. Conséquence : **pas de vérification à la compilation** ; une faute de frappe dans un nom de membre SAPI ne se voit qu'à l'exécution, par une exception.

Les collections COM sont parcourues par index (`Count` / `Item(i)`), plus fiable qu'un `foreach` sur un `dynamic`.

## Principe central : tout est une relance

Avec les voix naturelles, SAPI :

- n'applique **vitesse, timbre et voix qu'au lancement** d'une lecture ;
- a une **pause native qui saute un bout de texte**.

Le moteur ne s'appuie donc jamais sur l'état interne de SAPI. Il garde lui-même le texte et une position, et toute commande se ramène à `Parler(position)` :

| Commande | Implémentation |
|---|---|
| `Lire(texte, depart)` | `Parler(depart)` |
| `Pause()` | Mot en cours → `CouperSapi()` → `_positionReprise` = début du **mot précédent** (reprise avec un peu de contexte) |
| `Reprendre()` | `Parler(_positionReprise)` |
| `AllerA(pos)` | En lecture : `Parler(debutDuMot)` ; en pause : déplace `_positionReprise` |
| `Relancer()` | En lecture : `Parler(motEnCours)` ; en pause : rien (la reprise prendra les nouveaux réglages) |
| `Arreter()` | Oublie la reprise, `CouperSapi()` |

`CouperSapi()` = `Speak("", Async | Purge)` : parler une chaîne vide en vidant la file.

Les setters `Vitesse`, `Timbre`, `VoixActuelle` **ne font que mémoriser**. Ils n'ont d'effet qu'au prochain `Parler`.

## États

| Propriété | Vrai quand |
|---|---|
| `EnPause` | `_positionReprise != null` |
| `EnLecture` | pas en pause **et** `Status.RunningState != SRSEDone (1)` |
| `EstOccupe` | `EnPause || EnLecture` |

En pause, SAPI est réellement arrêté ; la pause n'existe que dans `MoteurVocal`.

## Ce qui est envoyé à SAPI

```
<pitch absmiddle="T">  +  texte[depart..] échappé  +  </pitch>
└──── préfixe ────────┘
```

Drapeaux : `SVSFlagsAsync (1) | SVSFPurgeBeforeSpeak (2) | SVSFIsXML (8)`.

- **Timbre** : pas de propriété SAPI, d'où la balise XML `<pitch>`, d'où le mode XML, d'où l'échappement.
- **Vitesse** : propriété `Rate`, bornée à [-10, 10].
- **Voix** : `_voix.Voice = jeton`, seulement si elle a changé (`_voixAppliquee`).

### Échappement (`EchapperPourXml`)

| Caractère | Devient |
|---|---|
| `&` `<` `>` | `&amp;` `&lt;` `&gt;` |
| Caractère de contrôle (sauf `\t` `\n` `\r`) | espace — sinon SAPI rejette **tout** le texte |

## Correspondance des positions (surlignage)

SAPI donne la position du mot prononcé (`Status.InputWordPosition` / `InputWordLength`) **dans la chaîne envoyée**, pas dans le texte de l'utilisateur. Trois décalages à défaire :

```
position SAPI
   − _longueurPrefixe          (la balise <pitch ...>)
   → _versOriginal[i]          (tableau : index dans la chaîne échappée → index dans le texte[depart..])
   + _depart                   (la lecture a commencé au milieu du texte)
   = position dans le texte complet
```

`_versOriginal` est construit pendant l'échappement : un `&` devenu `&amp;` produit 5 entrées pointant toutes sur le même caractère d'origine.

Toute modification du préfixe ou de l'échappement doit garder ce tableau cohérent, sinon le surlignage se décale.

## Notion de « mot »

Une suite de caractères sans espace blanc, ponctuation collée incluse. Fonctions : `DebutDuMot`, `DebutDuMotPrecedent`, `LongueurDuMot`. Utilisée pour les sauts, la pause et la longueur du mot surligné en pause.

## Vitesse : crans SAPI → multiplicateur réel

SAPI n'accepte que des **crans entiers**, quelle que soit la méthode (propriété `Rate`, balise `<rate>`, SSML `<prosody>`) : un ×1,5 demandé est arrondi à un cran. Mesures faites sur Denise (durée d'un même texte rendu en `.wav`), table `MultiplicateursVitesse` dans [FenetrePrincipale.cs](../src/FenetrePrincipale.cs) :

| Cran | -5 | -4 | -3 | -2 | -1 | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ×   | 0,67 | 0,74 | 0,80 | 0,87 | 0,94 | 1,0 | 1,2 | 1,4 | **1,6** | 1,8 | 2,0 | 2,2 | 2,4 | 2,6 | 2,8 | 3,0 |

- Le curseur de l'UI va de -5 à 10 (en dessous, trop lent pour être utile).
- Défaut : cran 3 (×1,6).
- Valeurs approximatives pour les autres voix.
- Conséquence : **pas de ×1,65 possible**, seulement ×1,6 ou ×1,8.

## Cycle de vie

- Construction : crée `SpVoice`, énumère les voix **une seule fois**. Une voix installée ensuite n'apparaît qu'après redémarrage de l'appli.
- `Dispose()` : `Arreter()` (exception COM ignorée) puis `Marshal.FinalReleaseComObject`.
