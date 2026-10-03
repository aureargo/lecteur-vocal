using System.Diagnostics;
using Microsoft.Win32;

namespace LecteurVocal;

/// <summary>
/// Guide d'installation de nouvelles voix, avec des boutons vers les pages utiles
/// et un bouton pour l'écouter plutôt que le lire.
/// </summary>
public sealed class FenetreAideVoix : Form
{
    private const string PageVoix = "https://github.com/gexgd0419/NaturalVoiceSAPIAdapter/wiki/Narrator-natural-voice-download-links";
    private const string PageAdaptateur = "https://github.com/gexgd0419/NaturalVoiceSAPIAdapter/releases";

    // Texte écrit pour être aussi écouté : pas de symboles que la voix lirait mal.
    private const string Guide =
        """
        Installer de nouvelles voix

        Le Lecteur vocal utilise les voix de Windows. Les plus belles voix gratuites et hors ligne sont les voix naturelles de Microsoft, comme Denise et Henri. Elles deviennent utilisables grâce à l'outil gratuit NaturalVoiceSAPIAdapter.

        Attention : les voix HD, comme Vivienne et Remy, et les versions récentes du Microsoft Store, ne fonctionnent pas. Elles sont réservées au Narrateur de Windows.

        Étape 1. Installer NaturalVoiceSAPIAdapter, une seule fois par PC.
            Ouvre la page de l'outil avec le bouton ci-dessous, et télécharge le fichier zip de la dernière version.
            Décompresse-le dans un dossier définitif, par exemple C:\Outils\NaturalVoice.
            Fais un clic droit sur Installer.exe, puis Exécuter en tant qu'administrateur.
            Installe la version 64 bits, et aussi la version 32 bits si tu utilises Balabolka.
            Garde coché Local Narrator voices, et décoche Edge online voices, qui passent par Internet.
            Règle Local voice path sur le dossier des voix, par exemple C:\Voix.

        Étape 2. Télécharger une voix.
            Ouvre la page des voix avec le bouton ci-dessous, et télécharge le fichier msix de la voix voulue.

        Étape 3. Installer la voix.
            Renomme le fichier msix en zip.
            Décompresse-le dans un nouveau sous-dossier du dossier des voix, par exemple C:\Voix\Denise.
            Le chemin ne doit contenir aucun accent, et le dossier des voix ne doit contenir que des sous-dossiers de voix.

        Étape 4. Utiliser la voix.
            Quitte le Lecteur vocal, avec un clic droit sur son icône près de l'horloge, puis Quitter. Relance-le ensuite.
            La nouvelle voix apparaît dans la liste des voix.
        """;

    public FenetreAideVoix(Action<string> ecouter)
    {
        Text = "Installer de nouvelles voix";
        Size = new Size(780, 620);
        MinimumSize = new Size(500, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 10f);

        var texte = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12f),
            BackColor = SystemColors.Window,
            // Les retours à la ligne du texte brut sont des "\n" : la zone de texte Windows veut des "\r\n".
            Text = Guide.ReplaceLineEndings("\r\n"),
        };

        string dossierVoix = DossierDesVoix();
        var boutons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(6) };
        boutons.Controls.Add(CreerBouton("🔊  Écouter ce guide", () => ecouter(Guide)));
        boutons.Controls.Add(CreerBouton("Page de l'outil", () => OuvrirAvecWindows(PageAdaptateur)));
        boutons.Controls.Add(CreerBouton("Page des voix", () => OuvrirAvecWindows(PageVoix)));
        boutons.Controls.Add(CreerBouton($"Ouvrir le dossier des voix ({dossierVoix})", () =>
        {
            Directory.CreateDirectory(dossierVoix);
            OuvrirAvecWindows(dossierVoix);
        }));

        Controls.Add(texte);
        Controls.Add(boutons);
    }

    private static Button CreerBouton(string texte, Action action)
    {
        var bouton = new Button { Text = texte, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        bouton.Click += (_, _) => action();
        return bouton;
    }

    /// <summary>
    /// UseShellExecute = true : Windows ouvre le lien ou le dossier avec le programme associé
    /// (navigateur par défaut, explorateur de fichiers), comme un double-clic.
    /// </summary>
    private static void OuvrirAvecWindows(string cible) =>
        Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true });

    /// <summary>Dossier des voix réglé dans NaturalVoiceSAPIAdapter (C:\Voix si rien n'est réglé).</summary>
    private static string DossierDesVoix()
    {
        using RegistryKey? cle = Registry.CurrentUser.OpenSubKey(@"Software\NaturalVoiceSAPIAdapter\Enumerator");
        string? chemin = cle?.GetValue("NarratorVoicePath") as string;
        return string.IsNullOrWhiteSpace(chemin) ? @"C:\Voix" : chemin;
    }
}
