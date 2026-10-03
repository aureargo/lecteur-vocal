using System.Runtime.InteropServices;

namespace LecteurVocal;

/// <summary>
/// La fenêtre de l'application : une grande zone de texte, des boutons de lecture
/// et les réglages de voix. Construite entièrement en code (sans le concepteur visuel
/// de Visual Studio) pour que tout soit lisible au même endroit.
/// </summary>
public sealed class FenetrePrincipale : Form
{
    private readonly MoteurVocal _moteur = new();

    private readonly TextBox _zoneTexte = new();
    private readonly Button _boutonLire = new();
    private readonly Button _boutonPause = new();
    private readonly Button _boutonStop = new();
    private readonly ComboBox _listeVoix = new();
    private readonly TrackBar _curseurVitesse = new();
    private readonly TrackBar _curseurTimbre = new();
    private readonly Label _valeurVitesse = new();
    private readonly Label _valeurTimbre = new();
    private readonly Label _barreEtat = new();

    // Minuteur de l'interface : toutes les 50 ms, on regarde où en est la voix
    // pour mettre à jour les boutons. Il tourne sur le thread de l'interface,
    // donc on peut toucher aux contrôles sans précaution particulière.
    private readonly System.Windows.Forms.Timer _minuteur = new() { Interval = 50 };

    public FenetrePrincipale()
    {
        Text = "Lecteur vocal";
        Size = new Size(760, 560);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);

        ConstruireInterface();
        RemplirReglages();

        _minuteur.Tick += (_, _) => MettreAJourEtat();
        _minuteur.Start();
    }

    // ------------------------------------------------------------------ Construction

    private void ConstruireInterface()
    {
        // --- Zone de texte : elle occupe tout l'espace restant (Dock = Fill).
        _zoneTexte.Multiline = true;
        _zoneTexte.ScrollBars = ScrollBars.Vertical;
        _zoneTexte.Dock = DockStyle.Fill;
        _zoneTexte.Font = new Font("Segoe UI", 13f);
        _zoneTexte.PlaceholderText = "Colle ici le texte à lire (Ctrl+V), puis clique sur Lire.";
        // Garder la sélection visible même quand la fenêtre n'a pas le focus :
        // c'est elle qui surlignera le mot en cours de lecture.
        _zoneTexte.HideSelection = false;

        // --- Boutons de lecture, alignés de gauche à droite.
        ConfigurerBouton(_boutonLire, "▶  Lire", (_, _) => LireZoneTexte());
        ConfigurerBouton(_boutonPause, "⏸  Pause", (_, _) => _moteur.PauseOuReprise());
        ConfigurerBouton(_boutonStop, "⏹  Stop", (_, _) => _moteur.Arreter());
        var rangeeBoutons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        rangeeBoutons.Controls.AddRange([_boutonLire, _boutonPause, _boutonStop]);

        // --- Réglages : un tableau à 3 colonnes (libellé | contrôle | valeur).
        _listeVoix.DropDownStyle = ComboBoxStyle.DropDownList;   // choix dans la liste uniquement
        _listeVoix.Dock = DockStyle.Fill;

        // Vitesse SAPI de -5 à 10. En dessous de -5, c'est trop lent pour être utile.
        ConfigurerCurseur(_curseurVitesse, -5, 10);
        ConfigurerCurseur(_curseurTimbre, -10, 10);

        var reglages = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(8) };
        reglages.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        reglages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        reglages.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        reglages.Controls.Add(rangeeBoutons, 0, 0);
        reglages.SetColumnSpan(rangeeBoutons, 3);
        AjouterLigne(reglages, 1, "Voix", _listeVoix, null);
        AjouterLigne(reglages, 2, "Vitesse", _curseurVitesse, _valeurVitesse);
        AjouterLigne(reglages, 3, "Timbre", _curseurTimbre, _valeurTimbre);

        _barreEtat.Dock = DockStyle.Bottom;
        _barreEtat.AutoSize = false;
        _barreEtat.Height = 28;
        _barreEtat.Padding = new Padding(8, 0, 8, 0);
        _barreEtat.TextAlign = ContentAlignment.MiddleLeft;
        _barreEtat.ForeColor = SystemColors.GrayText;

        // L'ordre d'ajout compte avec Dock : le contrôle « Fill » doit être ajouté en premier
        // pour qu'il prenne la place laissée libre par ceux docké en bas.
        Controls.Add(_zoneTexte);
        Controls.Add(reglages);
        Controls.Add(_barreEtat);

        // Les événements des réglages sont branchés après le remplissage initial
        // (voir RemplirReglages) pour ne pas se déclencher pendant l'initialisation.
    }

    private static void ConfigurerBouton(Button bouton, string texte, EventHandler auClic)
    {
        bouton.Text = texte;
        bouton.AutoSize = true;
        bouton.Padding = new Padding(10, 4, 10, 4);
        bouton.Click += auClic;
    }

    private static void ConfigurerCurseur(TrackBar curseur, int min, int max)
    {
        curseur.Minimum = min;
        curseur.Maximum = max;
        curseur.TickFrequency = 1;
        curseur.SmallChange = 1;
        curseur.LargeChange = 2;
        curseur.Dock = DockStyle.Fill;
    }

    private static void AjouterLigne(TableLayoutPanel tableau, int ligne, string libelle, Control controle, Label? valeur)
    {
        tableau.Controls.Add(new Label { Text = libelle, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) }, 0, ligne);
        tableau.Controls.Add(controle, 1, ligne);
        if (valeur != null)
        {
            valeur.AutoSize = true;
            valeur.Anchor = AnchorStyles.Left;
            tableau.Controls.Add(valeur, 2, ligne);
        }
    }

    private void RemplirReglages()
    {
        foreach (string nom in _moteur.Voix) _listeVoix.Items.Add(nom);

        // Voix par défaut : Denise si elle est installée, sinon la voix actuelle de Windows.
        string? denise = _moteur.Voix.FirstOrDefault(v => v.Contains("Denise"));
        _listeVoix.SelectedItem = denise ?? _moteur.VoixActuelle;
        _moteur.VoixActuelle = (string)_listeVoix.SelectedItem!;

        _curseurVitesse.Value = 3;   // ≈ ×1,6
        _curseurTimbre.Value = 0;
        AppliquerVitesse();
        AppliquerTimbre();

        _listeVoix.SelectedIndexChanged += (_, _) => _moteur.VoixActuelle = (string)_listeVoix.SelectedItem!;
        _curseurVitesse.ValueChanged += (_, _) => AppliquerVitesse();
        _curseurTimbre.ValueChanged += (_, _) => AppliquerTimbre();
    }

    // ------------------------------------------------------------------ Réglages

    private void AppliquerVitesse()
    {
        _moteur.Vitesse = _curseurVitesse.Value;
        _valeurVitesse.Text = LibelleVitesse(_curseurVitesse.Value);
    }

    private void AppliquerTimbre()
    {
        _moteur.Timbre = _curseurTimbre.Value;
        // Le « + » explicite pour les valeurs positives : "+3", "0", "-3".
        _valeurTimbre.Text = _curseurTimbre.Value.ToString("+0;-0;0");
    }

    /// <summary>
    /// Traduit la vitesse SAPI en multiplicateur, d'après des mesures faites sur Denise :
    /// 3 → ×1,6 ; 5 → ×2 ; 10 → ×3 ; -5 → ×0,67. C'est approximatif pour les autres voix.
    /// </summary>
    private static string LibelleVitesse(int vitesse) =>
        vitesse >= 0
            ? $"≈ ×{1 + vitesse / 5.0:0.0}"
            : $"≈ ×{1 / (1 - vitesse / 10.0):0.00}";

    // ------------------------------------------------------------------ Lecture

    private void LireZoneTexte()
    {
        string texte = _zoneTexte.Text;
        if (string.IsNullOrWhiteSpace(texte))
        {
            _barreEtat.Text = "Rien à lire : colle d'abord un texte.";
            return;
        }
        LireTexte(texte);
    }

    private void LireTexte(string texte)
    {
        try
        {
            _moteur.Lire(texte);
        }
        catch (COMException ex)
        {
            // Une erreur SAPI ne doit pas faire planter l'application : on l'affiche.
            _barreEtat.Text = $"Erreur de la voix : {ex.Message}";
        }
    }

    /// <summary>Appelée par le minuteur : met les boutons en accord avec l'état de la voix.</summary>
    private void MettreAJourEtat()
    {
        bool occupe = _moteur.EstOccupe;

        _boutonPause.Enabled = occupe;
        _boutonStop.Enabled = occupe;
        _boutonPause.Text = _moteur.EnPause ? "▶  Reprendre" : "⏸  Pause";

        _barreEtat.Text = occupe
            ? (_moteur.EnPause ? "En pause." : "Lecture en cours…")
            : (_barreEtat.Text.StartsWith("Erreur") || _barreEtat.Text.StartsWith("Rien") ? _barreEtat.Text : "Prêt.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _minuteur.Stop();
        _moteur.Dispose();
        base.OnFormClosed(e);
    }
}
