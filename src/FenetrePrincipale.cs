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
    // pour mettre à jour les boutons et le surlignage. Il tourne sur le thread de l'interface,
    // donc on peut toucher aux contrôles sans précaution particulière.
    private readonly System.Windows.Forms.Timer _minuteur = new() { Interval = 50 };

    // Quand on fait glisser un curseur, sa valeur change plusieurs fois de suite.
    // Plutôt que de relancer la voix à chaque cran, on attend 300 ms de calme
    // (technique appelée « debounce » : on relance le compte à rebours à chaque changement).
    private readonly System.Windows.Forms.Timer _minuteurRelance = new() { Interval = 300 };

    // La touche qui lit la sélection. null si elle n'a pas pu être réservée.
    private readonly RaccourciGlobal? _raccourci;
    private bool _captureEnCours;

    // Mémoire pour MettreAJourEtat : état précédent et dernier mot surligné.
    private bool _etaitOccupe;
    private (int Debut, int Longueur)? _dernierMotSurligne;

    // Endroit où le bouton de la souris a été enfoncé : sert à distinguer un clic d'un glisser.
    private Point _pointAppuiSouris;

    // Message ponctuel affiché dans la barre d'état (erreur, « rien à lire »...),
    // prioritaire sur l'état normal jusqu'à son expiration.
    private string? _message;
    private DateTime _messageExpire;

    private const string AideRaccourci = "Sélectionne du texte n'importe où et appuie sur la touche Pause pour l'écouter.";

    public FenetrePrincipale()
    {
        Text = "Lecteur vocal";
        Size = new Size(760, 560);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);

        ConstruireInterface();
        RemplirReglages();

        try
        {
            _raccourci = new RaccourciGlobal(Keys.Pause);
            _raccourci.Appuye += QuandRaccourciAppuye;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AfficherMessage(ex.Message, secondes: 30);
        }

        _minuteur.Tick += (_, _) => MettreAJourEtat();
        _minuteur.Start();

        _minuteurRelance.Tick += (_, _) =>
        {
            _minuteurRelance.Stop();   // un seul déclenchement
            Executer(_moteur.Relancer);
        };
    }

    // ------------------------------------------------------------------ Construction

    private void ConstruireInterface()
    {
        // --- Zone de texte : elle occupe tout l'espace restant (Dock = Fill).
        _zoneTexte.Multiline = true;
        _zoneTexte.ScrollBars = ScrollBars.Vertical;
        _zoneTexte.Dock = DockStyle.Fill;
        _zoneTexte.Font = new Font("Segoe UI", 13f);
        _zoneTexte.PlaceholderText = "Colle ici le texte à lire (Ctrl+V), puis clique sur Lire.\r\n"
                                   + "Clique sur un mot pour commencer la lecture à cet endroit.";
        // Garder la sélection visible même quand la fenêtre n'a pas le focus :
        // c'est elle qui surligne le mot en cours de lecture.
        _zoneTexte.HideSelection = false;
        // Pendant la lecture, la zone passe en lecture seule (voir MettreAJourEtat) ;
        // on force le fond blanc pour qu'elle ne devienne pas grise.
        _zoneTexte.BackColor = SystemColors.Window;
        _zoneTexte.MouseDown += (_, e) => _pointAppuiSouris = e.Location;
        _zoneTexte.MouseUp += QuandClicDansTexte;

        // --- Boutons de lecture, alignés de gauche à droite.
        ConfigurerBouton(_boutonLire, "▶  Lire", (_, _) => LireZoneTexte());
        ConfigurerBouton(_boutonPause, "⏸  Pause", (_, _) => Executer(_moteur.PauseOuReprise));
        ConfigurerBouton(_boutonStop, "⏹  Stop", (_, _) => Executer(_moteur.Arreter));
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

        _listeVoix.SelectedIndexChanged += (_, _) =>
        {
            _moteur.VoixActuelle = (string)_listeVoix.SelectedItem!;
            DemanderRelance();
        };
        _curseurVitesse.ValueChanged += (_, _) => { AppliquerVitesse(); DemanderRelance(); };
        _curseurTimbre.ValueChanged += (_, _) => { AppliquerTimbre(); DemanderRelance(); };
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

    /// <summary>Applique les nouveaux réglages à la lecture en cours, après 300 ms sans changement.</summary>
    private void DemanderRelance()
    {
        _minuteurRelance.Stop();
        _minuteurRelance.Start();
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

    /// <summary>
    /// Bouton Lire : lit la zone de texte à partir du curseur (là où l'utilisateur a cliqué).
    /// Si le curseur est à la fin du texte (cas d'un texte qu'on vient de coller), on part du début.
    /// </summary>
    private void LireZoneTexte()
    {
        string texte = _zoneTexte.Text;
        if (string.IsNullOrWhiteSpace(texte))
        {
            AfficherMessage("Rien à lire : colle d'abord un texte.");
            return;
        }
        int depart = _zoneTexte.SelectionStart;
        if (depart >= texte.TrimEnd().Length) depart = 0;
        Executer(() => _moteur.Lire(texte, depart));
    }

    /// <summary>
    /// Clic dans le texte pendant une lecture ou une pause : on saute au mot cliqué.
    /// On ignore les glisser (sélection de texte) : la souris doit être relâchée là où elle a été enfoncée.
    /// </summary>
    private void QuandClicDansTexte(object? sender, MouseEventArgs e)
    {
        bool simpleClic = Math.Abs(e.X - _pointAppuiSouris.X) < 4 && Math.Abs(e.Y - _pointAppuiSouris.Y) < 4;
        if (e.Button != MouseButtons.Left || !simpleClic || !_moteur.EstOccupe) return;

        int position = _zoneTexte.GetCharIndexFromPosition(e.Location);
        Executer(() => _moteur.AllerA(position));
        _dernierMotSurligne = null;   // force le surlignage du nouveau mot
    }

    /// <summary>
    /// Touche Pause pressée (n'importe où dans Windows) :
    /// si une lecture est en cours, on l'arrête ; sinon, on lit le texte sélectionné.
    ///
    /// <c>async void</c> : réservé aux gestionnaires d'événements. Le <c>await</c> rend la main
    /// à l'interface pendant la copie, puis la suite s'exécute à nouveau sur le thread de l'interface.
    /// </summary>
    private async void QuandRaccourciAppuye()
    {
        if (_moteur.EstOccupe)
        {
            Executer(_moteur.Arreter);
            return;
        }
        if (_captureEnCours) return;   // appui répété pendant une copie : on ignore

        _captureEnCours = true;
        try
        {
            string? texte = await LecteurSelection.CopierSelectionAsync();
            if (string.IsNullOrWhiteSpace(texte))
            {
                AfficherMessage("Aucun texte sélectionné.");
                return;
            }
            // Le texte capturé s'affiche dans la fenêtre : on peut le relire ou le modifier ensuite.
            _zoneTexte.Text = texte;
            Executer(() => _moteur.Lire(texte));
        }
        finally
        {
            _captureEnCours = false;
        }
    }

    /// <summary>Exécute une commande de la voix ; une erreur SAPI s'affiche au lieu de faire planter l'application.</summary>
    private void Executer(Action commande)
    {
        try
        {
            commande();
        }
        catch (COMException ex)
        {
            AfficherMessage($"Erreur de la voix : {ex.Message}", secondes: 10);
        }
    }

    private void AfficherMessage(string message, int secondes = 4)
    {
        _message = message;
        _messageExpire = DateTime.Now.AddSeconds(secondes);
    }

    // ------------------------------------------------------------------ Mise à jour régulière

    /// <summary>Appelée par le minuteur : accorde l'interface avec l'état de la voix.</summary>
    private void MettreAJourEtat()
    {
        bool occupe = _moteur.EstOccupe;

        _boutonPause.Enabled = occupe;
        _boutonStop.Enabled = occupe;
        _boutonPause.Text = _moteur.EnPause ? "▶  Reprendre" : "⏸  Pause";

        // Pendant la lecture, le texte ne doit pas être modifié (les positions des mots
        // deviendraient fausses) : lecture seule. On peut quand même cliquer dedans.
        if (_zoneTexte.ReadOnly != occupe) _zoneTexte.ReadOnly = occupe;

        if (occupe)
        {
            SurlignerMotEnCours();
        }
        else if (_etaitOccupe)
        {
            // La lecture vient de finir (ou Stop) : on retire le surlignage
            // et on replace le curseur au début pour la prochaine lecture.
            _zoneTexte.Select(0, 0);
            _dernierMotSurligne = null;
        }
        _etaitOccupe = occupe;

        string etat;
        if (_message != null && DateTime.Now < _messageExpire) etat = _message;
        else if (_moteur.EnPause) etat = "En pause. Clique sur un mot pour choisir où reprendre.";
        else if (occupe) etat = "Lecture en cours…  (clic sur un mot = y sauter ; touche Pause = arrêter)";
        else etat = AideRaccourci;

        // On ne réaffecte le texte que s'il change, pour éviter un scintillement.
        if (_barreEtat.Text != etat) _barreEtat.Text = etat;
    }

    /// <summary>Sélectionne (donc surligne) le mot prononcé, et fait défiler le texte pour le garder visible.</summary>
    private void SurlignerMotEnCours()
    {
        // Pendant que l'utilisateur clique, on ne touche pas à la sélection pour ne pas le gêner.
        if (MouseButtons != MouseButtons.None) return;

        var mot = _moteur.MotEnCours();
        if (mot == null || mot == _dernierMotSurligne) return;
        _dernierMotSurligne = mot;

        var (debut, longueur) = mot.Value;
        if (debut + longueur > _zoneTexte.TextLength) return;   // sécurité
        _zoneTexte.Select(debut, longueur);
        _zoneTexte.ScrollToCaret();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _minuteur.Stop();
        _minuteurRelance.Stop();
        _raccourci?.Dispose();
        _moteur.Dispose();
        base.OnFormClosed(e);
    }
}
