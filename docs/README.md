# Documentation développeur — Lecteur vocal

Pour reprendre le projet. Le [README racine](../README.md) est la doc **utilisateur** ; ce dossier est la doc **développeur**.

## Le projet en 30 secondes

| | |
|---|---|
| **Ce que ça fait** | Lit à voix haute, hors ligne, le texte sélectionné dans n'importe quel logiciel (touche **Pause**) ou collé dans la fenêtre |
| **Pourquoi** | Remplacer Balabolka, qui lisait *chaque* copier-coller. Ici, seule la touche déclenche une lecture |
| **Techno** | C# 14 / .NET 10, WinForms, SAPI 5 (COM), P/Invoke `user32.dll` |
| **Voix visées** | Microsoft Denise / Henri (neuronales, locales), exposées à SAPI par [NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter) |
| **Taille** | 10 fichiers, ~1 600 lignes, aucune dépendance NuGet |
| **Plateforme** | Windows 11 uniquement |
| **Version** | `<Version>` du [.csproj](../LecteurVocal.csproj) (1.1.0) |
| **Tests** | Aucun test automatisé — [checklist manuelle](developpement.md#tests-manuels) |

## Par où commencer

| Ordre | Document | Contenu |
|---|---|---|
| 1 | [architecture.md](architecture.md) | Composants, flux principaux, threads, état persistant |
| 2 | [moteur-vocal.md](moteur-vocal.md) | SAPI, principe « tout est une relance », correspondance des positions, vitesse |
| 3 | [integration-windows.md](integration-windows.md) | Capture de la sélection, raccourci global, instance unique, zone de notification, démarrage |
| 4 | [developpement.md](developpement.md) | Compiler, lancer, installer, conventions, tests manuels |
| 5 | [decisions-et-pieges.md](decisions-et-pieges.md) | Pourquoi c'est fait ainsi, pièges à ne pas « corriger », limites, pistes |

## Les 5 règles à connaître avant de toucher au code

1. **SAPI n'applique rien en cours de lecture** avec ces voix. Pause, changement de vitesse/voix/timbre et saut à un mot = **arrêter puis relancer depuis une position**. → [moteur-vocal.md](moteur-vocal.md)
2. **Un Ctrl+C manuel ne doit jamais déclencher de lecture.** C'est la raison d'être du projet. → [integration-windows.md](integration-windows.md#capture-de-la-sélection)
3. **Le presse-papiers de l'utilisateur doit sortir intact** de la capture, sans doublon dans Win+V.
4. **Instance unique** : si la version installée tourne, `dotnet run` ne fait que réafficher celle-ci et s'arrête. Quitter l'installée d'abord. → [developpement.md](developpement.md#lancer-en-développement)
5. **Tout est en français** : identifiants, commentaires, messages, commits.

## Carte des fichiers

| Fichier | Rôle | Doc |
|---|---|---|
| [Program.cs](../src/Program.cs) | `Main`, instance unique, démarrage caché | [architecture](architecture.md#démarrage) |
| [FenetrePrincipale.cs](../src/FenetrePrincipale.cs) | Fenêtre, menus, icône de notification, orchestration | [architecture](architecture.md) |
| [MoteurVocal.cs](../src/MoteurVocal.cs) | Pilotage SAPI, position de lecture | [moteur-vocal](moteur-vocal.md) |
| [LecteurSelection.cs](../src/LecteurSelection.cs) | Capture de la sélection via le presse-papiers | [integration-windows](integration-windows.md#capture-de-la-sélection) |
| [RaccourciGlobal.cs](../src/RaccourciGlobal.cs) | Touche globale (`RegisterHotKey`) | [integration-windows](integration-windows.md#raccourci-global) |
| [Reglages.cs](../src/Reglages.cs) | Réglages JSON dans `%AppData%` | [architecture](architecture.md#état-persistant) |
| [DemarrageWindows.cs](../src/DemarrageWindows.cs) | Clé de registre `Run` | [integration-windows](integration-windows.md#lancement-au-démarrage) |
| [FenetreRaccourci.cs](../src/FenetreRaccourci.cs) | Dialogue de choix de la touche | [integration-windows](integration-windows.md#choix-de-la-touche) |
| [NomsTouches.cs](../src/NomsTouches.cs) | Noms français des touches, règle d'acceptabilité | [integration-windows](integration-windows.md#choix-de-la-touche) |
| [FenetreAideVoix.cs](../src/FenetreAideVoix.cs) | Guide d'installation des voix (écoutable) | [developpement](developpement.md#installer-les-voix) |
| [installer.cmd](../installer.cmd) | Compile et installe pour l'utilisateur courant | [developpement](developpement.md#installercmd) |
