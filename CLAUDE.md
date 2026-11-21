# Lecteur vocal

Application Windows 11 (C# 14 / .NET 10, WinForms, SAPI 5) qui lit à voix haute le texte
sélectionné dans n'importe quel logiciel quand on appuie sur une touche (Pause par défaut).

La documentation développeur est dans [docs/](docs/README.md). La lire avant de modifier le code,
en commençant par [docs/README.md](docs/README.md). Le [README.md](README.md) racine est la doc utilisateur.

## Règles à ne jamais enfreindre

1. **SAPI n'applique rien en cours de lecture** avec ces voix. Pause, vitesse, voix, timbre et saut
   à un mot = arrêter puis relancer depuis une position. Voir [docs/moteur-vocal.md](docs/moteur-vocal.md).
2. **Un Ctrl+C manuel ne doit jamais déclencher de lecture.** C'est la raison d'être du projet.
3. **Le presse-papiers de l'utilisateur doit sortir intact** de la capture de la sélection.
4. **Instance unique** : si la version installée tourne, `dotnet run` réaffiche celle-ci et s'arrête.
   Quitter la version installée avant de tester.
5. **Tout est en français** : identifiants, commentaires, messages, commits.

Avant de « corriger » un comportement qui semble étrange, consulter
[docs/decisions-et-pieges.md](docs/decisions-et-pieges.md) : beaucoup sont voulus.

## Commandes

| Action | Commande |
|---|---|
| Compiler | `dotnet build` |
| Lancer | `dotnet run` |
| Installer pour l'utilisateur | `installer.cmd` |

Aucun test automatisé : utiliser la checklist manuelle de [docs/developpement.md](docs/developpement.md#tests-manuels).

## Tenir la doc à jour

Toute modification de comportement, de fichier ou de réglage doit être reportée dans le fichier
concerné de `docs/` (la carte des fichiers de [docs/README.md](docs/README.md) indique lequel).
