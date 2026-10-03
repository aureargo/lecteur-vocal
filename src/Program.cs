namespace LecteurVocal;

internal static class Program
{
    // [STAThread] : les composants Windows (fenêtres, presse-papiers, objets COM comme
    // la synthèse vocale) exigent un thread en mode « STA » (Single-Threaded Apartment).
    // Sans cet attribut, le presse-papiers et SAPI lèvent des exceptions.
    [STAThread]
    private static void Main()
    {
        // Une seule instance à la fois : deux exemplaires se disputeraient la touche Pause.
        // Un Mutex nommé est visible par tout le système : le second lancement voit
        // qu'il existe déjà et s'arrête.
        using var mutex = new Mutex(true, "LecteurVocal-InstanceUnique", out bool premiereInstance);
        if (!premiereInstance)
        {
            MessageBox.Show("Le Lecteur vocal est déjà lancé.\nCherche son icône près de l'horloge.",
                            "Lecteur vocal", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Méthode générée automatiquement par .NET : applique les réglages du .csproj
        // (police par défaut, mise à l'échelle haute résolution, styles visuels).
        ApplicationConfiguration.Initialize();
        Application.Run(new FenetrePrincipale());
    }
}
