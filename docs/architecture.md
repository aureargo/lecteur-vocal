# Architecture

## Vue d'ensemble

```
Program.Main ── Mutex + EventWaitHandle nommés (instance unique)
     │
     ▼
FenetrePrincipale  ← chef d'orchestre, tout se passe sur le thread UI
     │
     ├── MoteurVocal         SAPI.SpVoice (COM, dynamic) + position dans le texte
     ├── RaccourciGlobal     RegisterHotKey → NativeWindow invisible → événement Appuye
     ├── LecteurSelection    Ctrl+C simulé + sauvegarde/restauration du presse-papiers
     ├── Reglages            %AppData%\LecteurVocal\reglages.json
     ├── DemarrageWindows    HKCU\...\CurrentVersion\Run
     ├── NotifyIcon          icône près de l'horloge + menu clic droit
     │
     ├── FenetreRaccourci (+ NomsTouches)   dialogue modal : choisir la touche
     └── FenetreAideVoix                    dialogue modal : guide des voix
```

**Principe** : les composants ne se connaissent pas entre eux. Seule `FenetrePrincipale` les relie. `MoteurVocal` ne connaît pas l'interface ; il expose un état (`EnLecture`, `EnPause`, `MotEnCours()`) que la fenêtre **interroge** toutes les 50 ms.

## Threads

| Thread | Ce qui y tourne |
|---|---|
| **UI (STA)** | Presque tout : fenêtre, minuteurs WinForms, `WM_HOTKEY`, appels SAPI, presse-papiers. `[STAThread]` est obligatoire pour COM et le presse-papiers |
| ThreadPool | Seulement l'attente du signal « afficher » d'un second lancement → `BeginInvoke(Afficher)` vers le thread UI |
| SAPI (interne) | SAPI parle en arrière-plan (`SVSFlagsAsync`) ; on ne reçoit **aucun événement** de lui, on lit son `Status` par sondage |

`QuandRaccourciAppuye` est `async void` : les `await` de la capture rendent la main à l'UI, la suite reprend sur le thread UI (contexte de synchronisation WinForms).

## Les deux minuteurs de `FenetrePrincipale`

| Minuteur | Intervalle | Rôle |
|---|---|---|
| `_minuteur` | 50 ms, permanent | `MettreAJourEtat()` : boutons, lecture seule de la zone, surlignage du mot, barre d'état |
| `_minuteurRelance` | 300 ms, *debounce* | Après un changement de voix/vitesse/timbre : `Relancer()` + enregistrer les réglages |

## Flux principaux

### Touche de lecture pressée (n'importe où)

1. Windows envoie `WM_HOTKEY` à la `NativeWindow` de `RaccourciGlobal` → événement `Appuye`.
2. `FenetrePrincipale.QuandRaccourciAppuye` :
   - lecture en cours ou en pause → `Arreter()`, fin ;
   - capture déjà en cours → ignoré ;
   - sinon `await LecteurSelection.CopierSelectionAsync()` (détail : [integration-windows.md](integration-windows.md#capture-de-la-sélection)).
3. Texte vide → message dans la barre d'état (+ son système si la fenêtre est cachée).
4. Sinon le texte est copié dans la zone de texte, puis `MoteurVocal.Lire(texte)`.

### Bouton Lire

Lit la zone à partir du curseur (`SelectionStart`). Si le curseur est en fin de texte (cas typique après un collage), on part de 0.

### Pendant la lecture (toutes les 50 ms)

1. `MoteurVocal.MotEnCours()` → `(Debut, Longueur)` dans le texte d'origine.
2. Si le mot a changé : `_zoneTexte.Select(...)` + `ScrollToCaret()`. Le surlignage **est** la sélection de la `TextBox` (`HideSelection = false`).
3. La zone passe en `ReadOnly` : modifier le texte fausserait les positions.
4. Pas de surlignage tant qu'un bouton de souris est enfoncé (ne pas gêner un clic).

### Interactions pendant la lecture

| Action | Code | Effet moteur |
|---|---|---|
| Bouton Pause / **Espace** | `PauseOuReprise()` | Pause : retient le mot précédent ; Reprise : relance depuis là |
| Clic simple sur un mot (< 4 px de déplacement) | `AllerA(SelectionStart)` (curseur posé par le clic) | Lecture : relance au mot ; Pause : change le point de reprise |
| Curseur vitesse/timbre, liste des voix | debounce 300 ms → `Relancer()` | Relance depuis le mot en cours |
| Bouton Stop / touche de lecture | `Arreter()` | Coupe SAPI, oublie la reprise |

Espace est intercepté dans `ProcessCmdKey` **seulement si** le moteur est occupé ; sinon il tape un espace normalement.

### Lecture du guide des voix

`LireTexteExterne` met `_lectureDeLaZone = false` : ni surlignage, ni clics, car le texte lu n'est pas celui de la zone. Fermer le guide arrête sa lecture.

### Erreurs SAPI

Toute commande du moteur passe par `Executer(Action)`, qui attrape `COMException` et l'affiche 10 s dans la barre d'état. L'appli ne plante pas sur une erreur de voix.

## Démarrage

```
Main(args)
 ├─ Mutex "LecteurVocal-InstanceUnique" déjà pris ?
 │    └─ oui → Set() sur EventWaitHandle "LecteurVocal-Afficher", return
 ├─ "--demarrage" dans args ? → démarrer caché
 └─ Application.Run(new FenetrePrincipale(signal, demarrerCache))
```

Constructeur de `FenetrePrincipale`, dans l'ordre :

1. `ConstruireInterface()` — tout en code, pas de designer. Ordre d'ajout important avec `Dock` : le `Fill` d'abord.
2. `CreerIconeNotification()`.
3. `RemplirReglages()` — voix mémorisée → sinon une voix contenant « Denise » → sinon voix Windows par défaut. Les événements des contrôles sont branchés **après** le remplissage.
4. `ActiverRaccourci()` — en cas d'échec (touche déjà prise) : message + bulle, l'appli continue sans raccourci ; la barre d'état et l'info-bulle de l'icône l'indiquent.
5. Démarrage des minuteurs, puis `_ = Handle` (force la création du handle même caché, requis par `BeginInvoke`) et enregistrement de l'attente du signal.

Démarrage caché : `SetVisibleCore` est surchargé et refuse d'afficher tant que `_affichageAutorise` est faux. `Afficher()` le passe à vrai.

## Fermeture

| Cause | Comportement |
|---|---|
| Croix (`UserClosing`) sans `_quitter` | `Cancel` + `Hide()`. Bulle explicative la 1re fois (`ReductionDejaExpliquee`) |
| Menu « Quitter » (fenêtre ou icône) | `_quitter = true` → `Close()` |
| Arrêt de Windows, fin de session | Fermeture réelle (raison ≠ `UserClosing`) |

`OnFormClosed` libère dans l'ordre : minuteurs, attente du signal, enregistre les réglages, masque et libère l'icône (sinon elle reste affichée jusqu'au survol), raccourci, moteur (`FinalReleaseComObject`).

## État persistant

| Quoi | Où | Écrit par |
|---|---|---|
| Réglages | `%AppData%\LecteurVocal\reglages.json` | `Reglages.Enregistrer()` |
| Lancement au démarrage | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valeur `LecteurVocal` = `"<chemin exe>" --demarrage` | `DemarrageWindows`, `installer.cmd` |
| Dossier des voix (lu seulement) | `HKCU\Software\NaturalVoiceSAPIAdapter\Enumerator`, valeur `NarratorVoicePath` (défaut `C:\Voix`) | NaturalVoiceSAPIAdapter |
| Application installée | `%LOCALAPPDATA%\Programs\LecteurVocal\LecteurVocal.exe` | `installer.cmd` |
| Raccourci menu Démarrer | `<Programs>\Lecteur vocal.lnk` | `installer.cmd` |

### `reglages.json`

```json
{
  "Voix": "<nom de la voix tel que renvoyé par SAPI>",
  "Vitesse": 3,
  "Timbre": 0,
  "Raccourci": "Pause",
  "ReductionDejaExpliquee": true
}
```

| Champ | Défaut | Remarque |
|---|---|---|
| `Voix` | `null` | Ignorée si la voix n'existe plus |
| `Vitesse` | `3` (≈ ×1,6) | Cran SAPI ; l'UI le borne à [-5, 10] |
| `Timbre` | `0` | [-10, 10] |
| `Raccourci` | `Pause` | Enum `Keys` en texte, ex. `"L, Control, Alt"` |
| `ReductionDejaExpliquee` | `false` | Bulle « l'appli reste active » déjà montrée |

Fichier absent ou illisible → valeurs par défaut, sans erreur. Écriture impossible → ignorée silencieusement.

**Quand c'est enregistré** : 300 ms après un changement de réglage, au changement de touche, après la 1re bulle, à la fermeture. Un `taskkill /F` (fait par `installer.cmd`) ne perd donc rien d'important.
