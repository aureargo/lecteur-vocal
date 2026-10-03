# Lecteur vocal

Lecture à voix haute, hors ligne, du texte collé ou sélectionné. Voix naturelles Microsoft (Denise, Henri).

## Installer / mettre à jour

Double-clic sur **`installer.cmd`**. Ensuite, l'appli est dans le **menu Démarrer** : « Lecteur vocal ».

## Utiliser

| Action | Effet |
|---|---|
| Sélectionner du texte n'importe où + touche **Pause** | Lit la sélection |
| Touche **Pause** pendant la lecture | Arrête |
| Coller un texte dans la fenêtre + **▶ Lire** | Lit à partir du curseur |
| Clic sur un mot (lecture ou pause) | Saute à ce mot |
| **Pause / Reprendre** | Reprend au mot précédent |
| Croix de la fenêtre | Cache la fenêtre, l'appli reste active près de l'horloge |
| Clic droit sur l'icône près de l'horloge | Ouvrir, lancer au démarrage de Windows, Quitter |

Un Ctrl+C normal ne déclenche **jamais** de lecture.

## Prérequis

- Windows 11, SDK .NET 10.
- Voix Denise / Henri rendues accessibles par [NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter)
  (anciennes versions des voix, dépaquetées dans `C:\Voix`). Les voix « HD » ne fonctionnent pas.

## Code

| Fichier | Rôle |
|---|---|
| `src/Program.cs` | Démarrage, instance unique |
| `src/FenetrePrincipale.cs` | Fenêtre, icône près de l'horloge |
| `src/MoteurVocal.cs` | Pilotage de SAPI (voix, position de lecture) |
| `src/LecteurSelection.cs` | Capture de la sélection via le presse-papiers |
| `src/RaccourciGlobal.cs` | Touche Pause globale |
| `src/Reglages.cs` | Réglages mémorisés (`%AppData%\LecteurVocal\reglages.json`) |
| `src/DemarrageWindows.cs` | Lancement à l'ouverture de session |
