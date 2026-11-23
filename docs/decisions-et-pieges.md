# Décisions, pièges et limites

## Décisions

| Date | Décision | Raison | Alternatives écartées |
|---|---|---|---|
| 2026-10-03 | Voix **Denise / Henri** (Microsoft, neuronales, locales) | Meilleure qualité gratuite hors ligne en français | Piper, Kokoro, Kyutai Pocket TTS (moins bons en français ou plus lourds) ; Edge (en ligne) ; Narrateur (inadapté) |
| 2026-10-03 | Anciennes versions des voix + NaturalVoiceSAPIAdapter, dans `C:\Voix` | Versions récentes et voix HD chiffrées, réservées au Narrateur | — |
| 2026-10-03 | **C# WinForms .NET 10** | Le plus simple pour raccourci global, voix SAPI, icône de notification | — |
| 2026-10-03 | SAPI en COM `dynamic` | `System.Speech` ne voit pas les voix de l'adaptateur | Fichier d'interop COM |
| 2026-10-03 | Lecture déclenchée **uniquement par une touche** | Défaut principal de Balabolka : lisait chaque copier-coller | Surveillance du presse-papiers |
| 2026-10-03 | Pause / réglages / saut = **relance depuis une position** | SAPI n'applique rien en cours de lecture ; sa pause saute du texte | Pause native SAPI |
| 2026-10-03 | Interface construite en code | Tout lisible au même endroit | Designer Visual Studio |
| 2026-10-03 | Croix = cacher, pas quitter | La touche doit rester active en permanence | — |
| 2026-10-03 | Installation par compilation locale (`installer.cmd`) | Usage personnel, pas de distribution | Installeur MSI, binaire publié |
| 2026-10-09 | `installer.cmd` propose le SDK via winget | Éviter l'échec muet sur un PC neuf | — |
| 2026-10-10 | Libellé de vitesse issu d'une **table mesurée** cran par cran | L'ancienne formule s'écartait des mesures pour les crans négatifs | Formules `1 + cran/5` et `1 / (1 − cran/10)` |

## Pièges — ne pas « corriger » sans savoir

Chaque ligne décrit un comportement qui semble étrange mais qui est voulu.

| Ce qu'on pourrait vouloir simplifier | Pourquoi ce n'est pas possible |
|---|---|
| Utiliser `System.Speech.Synthesis` | Ne voit pas Denise/Henri |
| Utiliser `SpVoice.Pause()` / `Resume()` | Saute un bout de texte avec les voix naturelles |
| Changer `Rate` pendant la lecture | Ignoré tant que la lecture n'est pas relancée |
| Demander ×1,5 via `<rate>` ou SSML `<prosody>` | SAPI arrondit à un cran entier de toute façon |
| Retirer le mode XML (`SVSFIsXML`) | Indispensable pour `<pitch>` (timbre) |
| Retirer `_versOriginal` / `_longueurPrefixe` | Le surlignage se décalerait (positions SAPI dans le texte échappé) |
| Garder les caractères de contrôle | SAPI rejette tout le texte XML |
| Remplacer `keybd_event` par `SendKeys.SendWait("^c")` | Pas fiable vers les autres programmes. `SendInput` serait la version moderne |
| Supprimer l'attente du relâchement des modificateurs | Avec Ctrl+Alt+L, le Ctrl+C simulé devient Ctrl+Alt+C |
| Garder une référence au `IDataObject` du presse-papiers | Devient invalide au prochain Ctrl+C : il faut copier chaque format |
| Supprimer les formats `CanIncludeInClipboardHistory` / `ExcludeClipboardContentFromMonitorProcessing` | Doublons dans Win+V |
| Attacher le raccourci à la fenêtre principale | Raccourci perdu quand WinForms recrée le handle |
| Garder le raccourci actif pendant `FenetreRaccourci` | Windows intercepte la touche, le dialogue ne la reçoit jamais |
| Retirer `[STAThread]` | Presse-papiers et SAPI lèvent des exceptions |
| Retirer `_ = Handle` dans le constructeur | `BeginInvoke` échoue si l'appli a démarré cachée |
| Laisser la zone de texte modifiable pendant la lecture | Les positions de mots deviendraient fausses |
| Retirer `MaxLength = 0` de la zone de texte | Ctrl+V tronqué sans prévenir à 32 767 caractères (défaut WinForms) |
| Trouver le mot cliqué avec `GetCharIndexFromPosition` | Bloqué à 65 535 (message Windows `EM_CHARFROMPOS` sur 16 bits). On lit `SelectionStart`, que le clic vient de poser |
| Ne traiter que `Keys.Pause` | Avec Ctrl, le clavier envoie `Keys.Cancel` |
| Laisser l'icône de notification sans `Visible = false` à la fermeture | Elle reste affichée jusqu'au survol |

## Limites connues

| Limite | Cause | Contournement |
|---|---|---|
| Vitesse par paliers (×1,6 ou ×1,8, pas ×1,65) | Crans entiers SAPI | Aucun côté SAPI |
| Voix HD (Vivienne, Remy) inutilisables | Réservées au Narrateur | Attendre Microsoft |
| Table de vitesse exacte pour Denise seulement | Mesures faites sur elle | Libellé marqué « ≈ » |
| Nouvelle voix visible seulement après redémarrage | Énumération au constructeur | Quitter / relancer |
| Pas de modificateur Win dans le raccourci | `MOD_WIN` non géré | Ctrl/Alt |
| Pas de lecture dans une appli qui bloque Ctrl+C | Technique du presse-papiers | Coller dans la fenêtre |
| Nécessite le SDK pour installer | Compilation locale | — |

## Dette technique et pistes

| Sujet | Détail | Priorité suggérée |
|---|---|---|
| Aucun test automatisé | La logique pure est testable : `EchapperPourXml`, `DebutDuMot`, `DebutDuMotPrecedent`, `NomsTouches.EstAcceptable`, `NomsTouches.Decrire`, `LibelleVitesse`. Il faudrait un projet de tests + `InternalsVisibleTo` (méthodes `private` aujourd'hui) | Moyenne |
| `keybd_event` obsolète | Remplacer par `SendInput` si un souci de fiabilité apparaît | Basse |
| Table de vitesse dans la fenêtre | Elle décrit le moteur ; pourrait vivre dans `MoteurVocal` | Basse |
| `FenetrePrincipale` fait ~700 lignes | Fichiers, menus et raccourci pourraient sortir en classes dédiées si elle grossit encore | Basse |
