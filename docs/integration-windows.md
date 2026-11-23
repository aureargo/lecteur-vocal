# Intégration Windows

Tout ce qui parle à Windows en dehors de la synthèse vocale.

## Capture de la sélection

Fichier : [src/LecteurSelection.cs](../src/LecteurSelection.cs). Windows n'offre pas de moyen universel de lire la sélection d'un autre programme. Technique utilisée (la plus répandue) :

| Étape | Détail |
|---|---|
| 1. Attendre le relâchement des modificateurs | Maj, Ctrl, Alt, Win (max 1,5 s). Sinon, avec un raccourci Ctrl+Alt+L, notre Ctrl+C deviendrait Ctrl+Alt+C |
| 2. Sauvegarder le presse-papiers | **Copie format par format** dans un `DataObject` à nous. Une simple référence deviendrait invalide au prochain Ctrl+C. Formats non copiables ignorés |
| 3. Noter `GetClipboardSequenceNumber()` | Compteur incrémenté par Windows à chaque modification |
| 4. Simuler Ctrl+C | `keybd_event` (Ctrl↓ C↓ C↑ Ctrl↑). `SendKeys.SendWait("^c")` n'était pas fiable vers les autres programmes |
| 5. Attendre la copie | Sondage du compteur toutes les 20 ms, max 600 ms. Inchangé → **aucune sélection**, on renvoie `null` sans toucher au presse-papiers |
| 6. Laisser 50 ms | Certaines applis écrivent plusieurs formats à la suite |
| 7. Lire le texte | `Clipboard.GetText()` |
| 8. Restaurer | Ancien contenu remis (ou presse-papiers vidé s'il était vide), **marqué comme ignorable** |

### Restauration « ignorable »

Deux formats spéciaux ajoutés au contenu restauré (valeur : entier 0 sur 4 octets, seule la présence compte) :

| Format | Effet |
|---|---|
| `CanIncludeInClipboardHistory` | Pas de doublon dans l'historique Win+V |
| `ExcludeClipboardContentFromMonitorProcessing` | Les logiciels qui surveillent le presse-papiers (s'ils respectent la convention) l'ignorent |

`SetDataObject(..., copy: true, retryTimes: 5, retryDelay: 100)` : les données survivent à la fermeture de l'appli.

### Robustesse

Le presse-papiers est partagé par tout le système. `EssayerPlusieursFois` réessaie 5 fois (30 ms) sur `ExternalException`. Si la restauration échoue malgré tout, on abandonne en silence.

### Limite connue

Seule la **restauration** est marquée ignorable. La copie faite par l'application cible en réponse au Ctrl+C simulé est une copie normale, que l'appli ne contrôle pas : un gestionnaire de presse-papiers tiers la voit passer, et elle apparaît probablement dans l'historique Win+V (hypothèse, à confirmer).

## Raccourci global

Fichier : [src/RaccourciGlobal.cs](../src/RaccourciGlobal.cs).

- `RegisterHotKey` → Windows envoie `WM_HOTKEY` à une fenêtre.
- Cette fenêtre est une **`NativeWindow` invisible dédiée**, pas la fenêtre principale : WinForms recrée parfois le handle de celle-ci, et le raccourci attaché à l'ancien handle serait perdu.
- `MOD_NOREPEAT` : touche maintenue = un seul déclenchement.
- Modificateurs : les bits `Keys.Control/Alt/Shift` sont traduits en `MOD_CONTROL/ALT/SHIFT`. **Win n'est pas géré.**
- Touche déjà prise par un autre programme → `Win32Exception` avec message en français.

## Choix de la touche

Fichiers : [src/FenetreRaccourci.cs](../src/FenetreRaccourci.cs), [src/NomsTouches.cs](../src/NomsTouches.cs).

Séquence dans `FenetrePrincipale.ChoisirRaccourci` :

1. **Libérer** le raccourci actuel (sinon Windows l'intercepte et le dialogue ne reçoit jamais la touche).
2. Dialogue modal ; `PreviewKeyDown → IsInputKey = true` pour recevoir aussi Tab, flèches, Entrée. Échap = annuler.
3. Réserver la nouvelle combinaison. Échec → **ancien raccourci restauré** s'il est différent et encore libre ; le message dit s'il l'est ou si aucune touche n'est active.
4. Enregistrer dans les réglages.

Règle `NomsTouches.EstAcceptable` :

| Combinaison | Acceptée ? |
|---|---|
| Modificateur seul | ❌ |
| Avec Ctrl ou Alt | ✅ |
| Pause, Arrêt défil, F1–F24 seules (ou + Maj) | ✅ |
| Lettre, chiffre, Espace… seuls | ❌ empêcherait d'écrire |

Piège : avec Ctrl enfoncé, le clavier envoie `Keys.Cancel` au lieu de `Keys.Pause`. `NomsTouches` traite les deux comme « Pause ».

## Instance unique

Fichier : [src/Program.cs](../src/Program.cs).

| Objet nommé (global au système) | Rôle |
|---|---|
| `Mutex "LecteurVocal-InstanceUnique"` | Détecte une instance déjà lancée (deux instances se disputeraient la touche) |
| `EventWaitHandle "LecteurVocal-Afficher"` (auto-reset) | Le 2e lancement le signale puis s'arrête ; la 1re instance affiche sa fenêtre |

Côté 1re instance : `ThreadPool.RegisterWaitForSingleObject` → `BeginInvoke(Afficher)`.

⚠️ Les noms sont les mêmes pour la version installée et la version de développement. Voir [developpement.md](developpement.md#lancer-en-développement).

## Zone de notification

- `NotifyIcon` : clic gauche = ouvrir ; clic droit = Ouvrir / Lancer au démarrage / Quitter.
- Info-bulle : « Lecteur vocal — <touche> pour lire la sélection ».
- Icône extraite du `.exe` (`ApplicationIcon` du `.csproj`), redimensionnée à `SystemInformation.SmallIconSize`.
- L'option « Lancer au démarrage » existe en deux exemplaires (menu de l'icône et menu Configuration), synchronisés à chaque clic.

## Lancement au démarrage

Fichier : [src/DemarrageWindows.cs](../src/DemarrageWindows.cs).

- Clé `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valeur `LecteurVocal` = `"<Environment.ProcessPath>" --demarrage`.
- HKCU : pas de droits administrateur.
- `--demarrage` → démarrage caché (voir [architecture.md](architecture.md#démarrage)).
- La valeur pointe vers l'exe **qui a activé l'option**. Si c'est une build Debug, elle pointe vers `bin\Debug\...`. `installer.cmd` la réécrit vers la version installée si elle existe.

## Fichiers texte

Dans `FenetrePrincipale` :

| Action | Encodage |
|---|---|
| Ouvrir (`LireFichierTexte`) | BOM reconnue et retirée (UTF-8, UTF-16 LE/BE) ; sans BOM, UTF-8 **strict** ; si invalide → Windows-1252 (fournisseur `CodePagesEncodingProvider` activé à la demande). Fins de ligne normalisées en `\r\n` |
| Enregistrer | UTF-8 sans BOM (`File.WriteAllText`) |

Filtre : `*.txt;*.md`, ou tous les fichiers. Ouvrir un fichier arrête la lecture en cours.
