# Lecteur vocal

Lecture à voix haute, hors ligne, du texte collé ou sélectionné. Voix naturelles Microsoft (Denise, Henri).

## Installer / mettre à jour

Double-clic sur **`installer.cmd`**. Ensuite, l'appli est dans le **menu Démarrer** : « Lecteur vocal ».

## Utiliser

| Action | Effet |
|---|---|
| Sélectionner du texte n'importe où + touche **Pause** (modifiable) | Lit la sélection |
| Touche **Pause** pendant la lecture | Arrête |
| Coller un texte dans la fenêtre + **▶ Lire** | Lit à partir du curseur |
| Clic sur un mot (lecture ou pause) | Saute à ce mot |
| **Pause / Reprendre** | Reprend au mot précédent |
| **Espace** pendant la lecture (fenêtre active) | Pause / reprise |
| Menu **Fichier** | Ouvrir un fichier texte, enregistrer le texte |
| Menu **Configuration** | Changer la touche de lecture (ou une combinaison), lancement au démarrage |
| Menu **Aide** | Guide d'installation de nouvelles voix (écoutable), À propos |
| Croix de la fenêtre | Cache la fenêtre, l'appli reste active près de l'horloge |
| Clic droit sur l'icône près de l'horloge | Ouvrir, lancer au démarrage de Windows, Quitter |

Un Ctrl+C normal ne déclenche **jamais** de lecture.

## Prérequis

- Windows 11, SDK .NET 10 (`installer.cmd` propose de l'installer via winget s'il manque).
- Voix Denise / Henri rendues accessibles par [NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter)
  (anciennes versions des voix, dépaquetées dans `C:\Voix`). Les voix « HD » ne fonctionnent pas.

## Code

Documentation développeur (architecture, pièges, tests manuels) : [`docs/`](docs/README.md).

| Fichier | Rôle |
|---|---|
| `src/Program.cs` | Démarrage, instance unique |
| `src/FenetrePrincipale.cs` | Fenêtre, icône près de l'horloge |
| `src/MoteurVocal.cs` | Pilotage de SAPI (voix, position de lecture) |
| `src/LecteurSelection.cs` | Capture de la sélection via le presse-papiers |
| `src/RaccourciGlobal.cs` | Touche Pause globale |
| `src/Reglages.cs` | Réglages mémorisés (`%AppData%\LecteurVocal\reglages.json`) |
| `src/DemarrageWindows.cs` | Lancement à l'ouverture de session |
| `src/FenetreRaccourci.cs`, `src/NomsTouches.cs` | Choix de la touche de lecture |
| `src/FenetreAideVoix.cs` | Guide d'installation des voix |
