using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LecteurVocal;

/// <summary>
/// Récupère le texte surligné dans l'application au premier plan.
///
/// Windows n'offre pas de moyen universel de lire « la sélection » d'un autre programme.
/// La technique fiable, utilisée par la plupart des logiciels de ce genre :
///   1. sauvegarder le contenu actuel du presse-papiers ;
///   2. simuler Ctrl+C pour que l'application copie sa sélection ;
///   3. lire le texte copié ;
///   4. remettre l'ancien contenu du presse-papiers.
/// Pour l'utilisateur, le presse-papiers n'a pas changé. Et à l'inverse, un Ctrl+C
/// fait à la main ne déclenche jamais de lecture : seul le raccourci le fait.
/// </summary>
public static class LecteurSelection
{
    // Compteur que Windows incrémente à chaque modification du presse-papiers.
    // Il permet de savoir si le Ctrl+C simulé a vraiment copié quelque chose.
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    // Simule l'appui ou le relâchement d'une touche, comme si elle venait du clavier.
    // (Officiellement remplacée par SendInput, mais plus simple et toujours supportée.)
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte touche, byte codeMateriel, uint drapeaux, UIntPtr infos);

    // Indique si une touche est enfoncée en ce moment (bit de poids fort du résultat).
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int touche);

    private const byte VK_SHIFT = 0x10;
    private const byte VK_MENU = 0x12;   // Alt
    private const byte VK_LWIN = 0x5B;
    private const byte VK_RWIN = 0x5C;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_C = 0x43;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const int AttenteMaxCopieMs = 600;

    /// <summary>
    /// Renvoie le texte sélectionné, ou null si rien n'est sélectionné.
    /// <c>async</c> : la méthode peut attendre (await) sans bloquer l'interface.
    /// </summary>
    public static async Task<string?> CopierSelectionAsync()
    {
        // Si le raccourci est une combinaison (ex. Ctrl + Alt + L), l'utilisateur tient peut-être
        // encore Alt : notre Ctrl+C deviendrait Ctrl+Alt+C. On attend que tout soit relâché.
        var attente = Stopwatch.StartNew();
        while (ModificateurEnfonce() && attente.ElapsedMilliseconds < 1500)
            await Task.Delay(15);

        DataObject? sauvegarde = SauvegarderPressePapiers();
        uint compteurAvant = GetClipboardSequenceNumber();

        SimulerCtrlC();

        // L'application cible copie à son rythme : on surveille le compteur.
        var chrono = Stopwatch.StartNew();
        while (GetClipboardSequenceNumber() == compteurAvant && chrono.ElapsedMilliseconds < AttenteMaxCopieMs)
            await Task.Delay(20);

        if (GetClipboardSequenceNumber() == compteurAvant)
            return null;   // rien n'a été copié : aucune sélection

        // Certaines applications écrivent plusieurs formats l'un après l'autre :
        // on leur laisse un instant pour terminer.
        await Task.Delay(50);

        string? texte = EssayerPlusieursFois(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);
        Restaurer(sauvegarde);
        return texte;
    }

    /// <summary>
    /// Envoie Ctrl+C à la fenêtre au premier plan : Ctrl enfoncé, C enfoncé, C relâché, Ctrl relâché.
    /// (SendKeys.SendWait("^c") de WinForms ne passait pas de façon fiable vers les autres programmes.)
    /// </summary>
    private static bool ModificateurEnfonce() =>
        new[] { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN }.Any(t => GetAsyncKeyState(t) < 0);

    private static void SimulerCtrlC()
    {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_C, 0, 0, UIntPtr.Zero);
        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>
    /// Copie tout le contenu du presse-papiers (texte, image, fichiers...) dans un objet à nous.
    /// Une simple référence ne suffirait pas : elle deviendrait invalide dès le prochain Ctrl+C.
    /// </summary>
    private static DataObject? SauvegarderPressePapiers()
    {
        IDataObject? actuel = EssayerPlusieursFois(Clipboard.GetDataObject);
        if (actuel == null) return null;

        var copie = new DataObject();
        foreach (string format in actuel.GetFormats(autoConvert: false))
        {
            try
            {
                object? donnee = actuel.GetData(format, autoConvert: false);
                if (donnee != null) copie.SetData(format, autoConvert: false, donnee);
            }
            catch (Exception)
            {
                // Certains formats exotiques ne se laissent pas copier : on les ignore,
                // l'essentiel (texte, images, fichiers) passe.
            }
        }
        return copie.GetFormats().Length > 0 ? copie : null;
    }

    private static void Restaurer(DataObject? sauvegarde)
    {
        try
        {
            if (sauvegarde == null)
            {
                Clipboard.Clear();   // le presse-papiers était vide avant : on le revide
            }
            else
            {
                MarquerCommeIgnorable(sauvegarde);
                // copy: true = les données restent disponibles même après la fermeture du programme.
                // Les deux derniers paramètres : 5 essais espacés de 100 ms si le presse-papiers est occupé.
                Clipboard.SetDataObject(sauvegarde, copy: true, retryTimes: 5, retryDelay: 100);
            }
        }
        catch (ExternalException)
        {
            // Presse-papiers bloqué par une autre application : tant pis, on ne restaure pas.
        }
    }

    /// <summary>
    /// Ajoute des formats spéciaux, reconnus par Windows, qui disent : « ce contenu n'est pas
    /// une nouvelle copie, ignorez-le ». L'historique Win+V ne crée pas de doublon, et les
    /// logiciels qui surveillent le presse-papiers (s'ils respectent la convention) ne réagissent pas.
    /// Seule la présence du format compte ; sa valeur est un entier 0 sur 4 octets.
    /// </summary>
    private static void MarquerCommeIgnorable(DataObject donnees)
    {
        donnees.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        donnees.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
    }

    /// <summary>
    /// Le presse-papiers est partagé par tout le système : si une autre application
    /// l'utilise à cet instant, l'accès échoue (ExternalException). On réessaie un peu.
    /// </summary>
    private static T? EssayerPlusieursFois<T>(Func<T?> action)
    {
        for (int essai = 0; essai < 5; essai++)
        {
            try { return action(); }
            catch (ExternalException) { Thread.Sleep(30); }
        }
        return default;
    }
}
