namespace LecteurVocal;

/// <summary>Noms lisibles des touches et combinaisons, en français : « Ctrl + Alt + L ».</summary>
public static class NomsTouches
{
    public static string Decrire(Keys combinaison)
    {
        var morceaux = new List<string>();
        if (combinaison.HasFlag(Keys.Control)) morceaux.Add("Ctrl");
        if (combinaison.HasFlag(Keys.Alt)) morceaux.Add("Alt");
        if (combinaison.HasFlag(Keys.Shift)) morceaux.Add("Maj");

        Keys touche = combinaison & Keys.KeyCode;
        if (touche != Keys.None) morceaux.Add(NomTouche(touche));
        return string.Join(" + ", morceaux);
    }

    private static string NomTouche(Keys touche) => touche switch
    {
        // Avec Ctrl enfoncé, le clavier envoie « Cancel » (Pause/Attn) au lieu de « Pause ».
        Keys.Pause or Keys.Cancel => "Pause",
        Keys.Scroll => "Arrêt défil",
        Keys.Space => "Espace",
        Keys.Return => "Entrée",
        Keys.Insert => "Inser",
        Keys.OemBackslash => "< / >",   // touche « <> » des claviers à 102 touches (AZERTY)
        >= Keys.D0 and <= Keys.D9 => ((int)(touche - Keys.D0)).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Pavé " + (int)(touche - Keys.NumPad0),
        _ => touche.ToString(),
    };

    /// <summary>
    /// Une combinaison est acceptable comme raccourci global si elle ne gêne pas la frappe normale :
    /// soit elle contient Ctrl ou Alt, soit c'est une touche qui ne sert pas à écrire (Pause, F1-F24...).
    /// </summary>
    public static bool EstAcceptable(Keys combinaison)
    {
        Keys touche = combinaison & Keys.KeyCode;
        if (touche is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return false;   // un modificateur seul n'est pas un raccourci
        if (combinaison.HasFlag(Keys.Control) || combinaison.HasFlag(Keys.Alt))
            return true;
        return touche is Keys.Pause or Keys.Cancel or Keys.Scroll or (>= Keys.F1 and <= Keys.F24);
    }
}
