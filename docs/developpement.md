# Développement

## Prérequis

| Outil | Pourquoi |
|---|---|
| Windows 11 | API Win32, SAPI, WinForms |
| **SDK .NET 10** (pas seulement le runtime) | Compilation. `installer.cmd` propose `winget install Microsoft.DotNet.SDK.10` s'il manque |
| NaturalVoiceSAPIAdapter + voix Denise/Henri | Facultatif pour compiler ; sans elles, l'appli utilise les voix SAPI par défaut (Hortense…) |
| Éditeur | VS Code ou Visual Studio. Aucun designer utilisé |

Aucune dépendance NuGet.

## Compiler

```powershell
dotnet build                 # Debug → bin\Debug\net10.0-windows\LecteurVocal.exe
dotnet build -c Release
```

`dotnet build` n'ouvre aucune fenêtre : sans risque pour vérifier que le code compile.

## Lancer en développement

```powershell
dotnet run
```

⚠️ **Quitter d'abord la version installée** (clic droit sur l'icône près de l'horloge → Quitter). Sinon, à cause de l'[instance unique](integration-windows.md#instance-unique), la version de dev réaffiche la version installée et s'arrête aussitôt : on croit tester son code, on teste l'ancien.

⚠️ Ne pas activer « Lancer au démarrage » depuis la version de dev : la clé `Run` pointerait vers `bin\Debug`.

### Pour un agent IA

**Ne pas lancer l'application soi-même** (`dotnet run`, exécution de l'exe, `installer.cmd`) : elle ouvre une fenêtre, prend le focus, réserve la touche Pause globale et simule des Ctrl+C. Le propriétaire utilise le PC en même temps. Compiler avec `dotnet build`, puis demander à l'utilisateur de tester.

## installer.cmd

Double-clic. À refaire après chaque modification pour mettre à jour la version installée.

| Étape | Commande / effet |
|---|---|
| 1. Trouver un SDK .NET 10 | `where dotnet`, sinon `%ProgramFiles%\dotnet\dotnet.exe` ; vérifie `--list-sdks` commence par `10.` |
| 2. Sinon, proposer winget | `winget install --id Microsoft.DotNet.SDK.10 -e`, puis recherche à nouveau (le `PATH` de la console n'est pas rafraîchi) |
| 3. Fermer l'appli | `taskkill /IM LecteurVocal.exe /F` |
| 4. Publier | `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o %LOCALAPPDATA%\Programs\LecteurVocal` |
| 5. Raccourci menu Démarrer | `Lecteur vocal.lnk` via `WScript.Shell` (PowerShell) |
| 6. Corriger la clé `Run` | Seulement si elle existe : la fait pointer vers l'exe installé |
| 7. Relancer l'appli | `start` |

Choix de publication : **un seul .exe, dépendant du runtime** (léger, mais .NET 10 doit être installé). Installation par utilisateur, sans droits administrateur.

Contraintes du fichier :

- Fins de ligne **CRLF** obligatoires (imposé par [.gitattributes](../.gitattributes)) ; `cmd` interprète mal un `.cmd` en LF.
- `chcp 65001` en tête pour les accents dans la console.

## Installer les voix

La source de vérité est le guide intégré : [FenetreAideVoix.cs](../src/FenetreAideVoix.cs) (menu Aide → Installer de nouvelles voix). Résumé :

1. Installer [NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter/releases) en administrateur (64 bits ; + 32 bits pour Balabolka). Cocher *Local Narrator voices*, décocher *Edge online voices*. *Local voice path* = `C:\Voix`.
2. Télécharger le `.msix` d'une voix ([liens](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter/wiki/Narrator-natural-voice-download-links)) — **anciennes versions** uniquement.
3. Renommer en `.zip`, décompresser dans `C:\Voix\<Nom>` (chemin sans accent ; `C:\Voix` ne contient que des sous-dossiers de voix).
4. Redémarrer le Lecteur vocal (les voix sont énumérées au lancement).

Les voix **HD** (Vivienne, Remy) et les versions récentes du Store ne fonctionnent pas.

Le texte du guide est écrit pour être **écouté** : pas de symboles que la voix lirait mal. Garder ce style en le modifiant.

## Conventions de code

| Sujet | Règle |
|---|---|
| Langue | **Français partout** : classes, méthodes, variables, commentaires, messages, commits |
| Exception | Constantes Win32/SAPI gardent leur nom officiel (`WM_HOTKEY`, `VK_C`, `MOD_NOREPEAT`) ou un nom français commenté avec l'officiel (`ParlerEnArrierePlan // SVSFlagsAsync`) |
| Commentaires | Expliquent le **pourquoi** et le **mécanisme** Windows/COM/C# (P/Invoke, STA, debounce, `async void`…). Garder ce niveau pédagogique |
| Interface | Construite **en code**, pas de fichiers `.Designer.cs` |
| Structure | Une classe par fichier dans `src/`, namespace `LecteurVocal` (file-scoped), classes `sealed` ou `static` |
| Nommage | Champs privés `_camelCase`, le reste en `PascalCase` |
| Options du projet | `Nullable` et `ImplicitUsings` activés, `PerMonitorV2` pour le DPI |
| Erreurs | `catch` ciblés (`COMException`, `IOException`, `ExternalException`…), avec un commentaire disant pourquoi on ignore. Jamais de plantage visible : message dans la barre d'état ou `MessageBox` |
| Commandes du moteur | Toujours via `Executer(...)` dans la fenêtre |

## Git

- Dépôt local, branche unique `master`, pas de remote (novembre 2026).
- **Un commit par étape fonctionnelle**, message en français : titre court + liste à puces si besoin.
- Mettre à jour le [README racine](../README.md) (tableaux « Utiliser » et « Code ») quand une fonction ou un fichier apparaît.
- Changer `<Version>` dans le `.csproj` pour une nouvelle version (affichée dans « À propos »).

## Tests manuels

Pas de tests automatisés. Checklist à dérouler après une modification, sur la version installée.

| # | Scénario | Attendu |
|---|---|---|
| 1 | Sélectionner du texte dans un navigateur + Pause | Lecture, texte copié dans la fenêtre |
| 2 | Pause pendant la lecture | Arrêt |
| 3 | Ctrl+C manuel | **Aucune** lecture |
| 4 | Copier une image, puis lire une sélection, puis Ctrl+V | L'image est toujours là |
| 5 | Win+V après une lecture | L'ancien contenu n'apparaît pas une 2e fois |
| 6 | Pause sans sélection | Message « Aucun texte sélectionné » (son si fenêtre cachée) |
| 7 | Coller un texte + Lire | Lit depuis le début |
| 8 | Cliquer au milieu + Lire | Lit depuis ce mot |
| 9 | Clic sur un mot pendant la lecture / en pause | Saut immédiat / reprise à ce mot |
| 10 | Espace pendant la lecture ; Espace à l'arrêt | Pause-reprise ; tape un espace |
| 11 | Bouger la vitesse en pleine lecture | Appliquée en ~300 ms, sans perte de position |
| 12 | Texte avec `& < >` et caractères de contrôle | Lu en entier, surlignage aligné |
| 13 | Croix de la fenêtre | Fenêtre cachée, bulle la 1re fois, la touche fonctionne |
| 14 | Relancer depuis le menu Démarrer | La fenêtre existante réapparaît |
| 15 | Touche de lecture : Ctrl+Alt+L ; puis une lettre seule ; puis une touche prise | Acceptée ; refusée ; message + ancien raccourci conservé |
| 16 | Ctrl+Alt+L sur une sélection | Lecture (attente du relâchement d'Alt) |
| 17 | Activer le démarrage, rouvrir la session | Appli lancée cachée |
| 18 | Ouvrir un `.txt` ANSI avec accents | Accents corrects |
| 19 | Aide → Écouter le guide, puis fermer | Lecture sans surlignage, arrêt à la fermeture |
| 20 | Fermer via Quitter, relancer | Voix, vitesse, timbre, touche mémorisés |
