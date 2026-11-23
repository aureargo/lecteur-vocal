namespace LecteurVocal;

internal static class Program
{
    // [STAThread] : les composants Windows (fenêtres, presse-papiers, objets COM comme
    // la synthèse vocale) exigent un thread en mode « STA » (Single-Threaded Apartment).
    // Sans cet attribut, le presse-papiers et SAPI lèvent des exceptions.
    [STAThread]
    private static void Main(string[] arguments)
    {
        // Une seule instance à la fois : deux exemplaires se disputeraient la touche de lecture.
        // Un Mutex nommé est visible par tout le système : le second lancement voit
        // qu'il existe déjà.
        using var mutex = new Mutex(true, "LecteurVocal-InstanceUnique", out bool premiereInstance);

        // Signal nommé, lui aussi visible par tout le système : le second lancement s'en sert
        // pour demander à l'instance déjà ouverte d'afficher sa fenêtre, puis s'arrête.
        using var signalAfficher = new EventWaitHandle(false, EventResetMode.AutoReset, "LecteurVocal-Afficher");
        if (!premiereInstance)
        {
            signalAfficher.Set();
            return;
        }

        // Lancé par Windows à l'ouverture de session : on démarre caché, dans la zone de notification.
        bool demarrerCache = arguments.Contains(DemarrageWindows.ArgumentDemarrage);

        // Méthode générée automatiquement par .NET : applique les réglages du .csproj
        // (police par défaut, mise à l'échelle haute résolution, styles visuels).
        ApplicationConfiguration.Initialize();
        Application.Run(new FenetrePrincipale(signalAfficher, demarrerCache));
    }
}
