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
    private readonly Reglages _reglages = Reglages.Charger();

    // Icône dans la zone de notification (près de l'horloge), avec son menu clic droit.
    private readonly NotifyIcon _iconeNotification = new();
    private readonly ToolStripMenuItem _menuDemarrage = new("Lancer au démarrage de Windows");
    // La même option, dans le menu Configuration de la fenêtre (les deux restent synchronisées).
    private readonly ToolStripMenuItem _menuDemarrageConfig = new("Lancer au démarrage de Windows");

    // Signal envoyé par un second lancement du programme (voir Program.cs) pour afficher la fenêtre.
    private readonly RegisteredWaitHandle _attenteSignal;

    // false tant que la fenêtre doit rester cachée (lancement au démarrage de Windows).
    private bool _affichageAutorise;
    // true quand l'utilisateur choisit « Quitter » : fermer la fenêtre arrête alors vraiment l'appli.
    private bool _quitter;

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
    private RaccourciGlobal? _raccourci;
    private bool _captureEnCours;

    // false quand la voix lit un texte qui n'est pas celui de la zone (le guide des voix) :
    // il ne faut alors ni surligner ni réagir aux clics dans la zone.
    private bool _lectureDeLaZone = true;

    // Mémoire pour MettreAJourEtat : état précédent et dernier mot surligné.
    private bool _etaitOccupe;
    private (int Debut, int Longueur)? _dernierMotSurligne;

    // Endroit où le bouton de la souris a été enfoncé : sert à distinguer un clic d'un glisser.
    private Point _pointAppuiSouris;

    // Message ponctuel affiché dans la barre d'état (erreur, « rien à lire »...),
    // prioritaire sur l'état normal jusqu'à son expiration.
    private string? _message;
    private DateTime _messageExpire;

    /// <summary>Nom lisible du raccourci actuel, par exemple « Pause » ou « Ctrl + Alt + L ».</summary>
    private string NomRaccourci => NomsTouches.Decrire(_reglages.Raccourci);

    public FenetrePrincipale(EventWaitHandle signalAfficher, bool demarrerCache)
    {
        Text = "Lecteur vocal";
        // Reprend l'icône intégrée au .exe (voir ApplicationIcon dans le .csproj).
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        Size = new Size(760, 560);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        _affichageAutorise = !demarrerCache;

        ConstruireInterface();
        CreerIconeNotification();
        RemplirReglages();

        if (!ActiverRaccourci(_reglages.Raccourci, out string? erreur))
        {
            AfficherMessage(erreur, secondes: 30);
            _iconeNotification.ShowBalloonTip(5000, "Lecteur vocal", erreur, ToolTipIcon.Warning);
        }

        _minuteur.Tick += (_, _) => MettreAJourEtat();
        _minuteur.Start();

        _minuteurRelance.Tick += (_, _) =>
        {
            _minuteurRelance.Stop();   // un seul déclenchement
            Executer(_moteur.Relancer);
            EnregistrerReglages();
        };

        // Le ThreadPool surveille le signal sur un thread à part. Quand il arrive, on ne peut pas
        // toucher à la fenêtre depuis ce thread : BeginInvoke confie Afficher au thread de l'interface.
        // On force d'abord la création de la fenêtre Windows (Handle), nécessaire à BeginInvoke
        // même quand la fenêtre démarre cachée.
        _ = Handle;
        _attenteSignal = ThreadPool.RegisterWaitForSingleObject(
            signalAfficher, (_, _) => BeginInvoke(Afficher), null, Timeout.Infinite, executeOnlyOnce: false);
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
        // Par défaut, Windows tronque sans prévenir un collage au-delà de 32 767 caractères.
        // 0 = pas de limite.
        _zoneTexte.MaxLength = 0;
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
        // pour qu'il prenne la place laissée libre par ceux dockés en haut et en bas.
        Controls.Add(_zoneTexte);
        Controls.Add(reglages);
        Controls.Add(_barreEtat);
        Controls.Add(CreerMenu());

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

    /// <summary>Barre de menus en haut de la fenêtre : Fichier, Configuration, Aide.</summary>
    private MenuStrip CreerMenu()
    {
        // Le « & » devant une lettre la souligne : Alt + cette lettre ouvre le menu au clavier.
        var fichier = new ToolStripMenuItem("&Fichier");
        fichier.DropDownItems.Add(new ToolStripMenuItem("&Ouvrir un fichier texte…", null, (_, _) => OuvrirFichier(), Keys.Control | Keys.O));
        fichier.DropDownItems.Add(new ToolStripMenuItem("&Enregistrer le texte sous…", null, (_, _) => EnregistrerFichier(), Keys.Control | Keys.S));
        fichier.DropDownItems.Add(new ToolStripSeparator());
        fichier.DropDownItems.Add(new ToolStripMenuItem("&Quitter", null, (_, _) => Quitter()));

        var configuration = new ToolStripMenuItem("&Configuration");
        configuration.DropDownItems.Add(new ToolStripMenuItem("&Touche de lecture…", null, (_, _) => ChoisirRaccourci()));
        configuration.DropDownItems.Add(_menuDemarrageConfig);

        var aide = new ToolStripMenuItem("&Aide");
        aide.DropDownItems.Add(new ToolStripMenuItem("&Installer de nouvelles voix…", null, (_, _) => AfficherAideVoix()));
        aide.DropDownItems.Add(new ToolStripMenuItem("À &propos", null, (_, _) => AfficherAPropos()));

        var menu = new MenuStrip { Dock = DockStyle.Top };
        menu.Items.AddRange([fichier, configuration, aide]);
        MainMenuStrip = menu;   // menu principal de la fenêtre (gère la touche Alt)
        return menu;
    }

    /// <summary>Crée l'icône près de l'horloge et son menu (clic droit).</summary>
    private void CreerIconeNotification()
    {
        foreach (var option in new[] { _menuDemarrage, _menuDemarrageConfig })
        {
            option.Checked = DemarrageWindows.EstActive;
            option.Click += (_, _) =>
            {
                DemarrageWindows.Activer(!DemarrageWindows.EstActive);
                _menuDemarrage.Checked = _menuDemarrageConfig.Checked = DemarrageWindows.EstActive;
            };
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir", null, (_, _) => Afficher());
        menu.Items.Add(_menuDemarrage);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Quitter());

        // Icône en petite taille (16×16 à 100 %, plus grand sur les écrans mis à l'échelle).
        _iconeNotification.Icon = new Icon(Icon!, SystemInformation.SmallIconSize);
        _iconeNotification.Text = "Lecteur vocal";   // complété avec le raccourci dans ActiverRaccourci
        _iconeNotification.ContextMenuStrip = menu;
        _iconeNotification.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Afficher(); };
        _iconeNotification.Visible = true;
    }

    private void RemplirReglages()
    {
        foreach (string nom in _moteur.Voix) _listeVoix.Items.Add(nom);

        // Voix : celle mémorisée si elle existe encore, sinon Denise, sinon la voix actuelle de Windows.
        string? voix = _moteur.Voix.FirstOrDefault(v => v == _reglages.Voix)
                       ?? _moteur.Voix.FirstOrDefault(v => v.Contains("Denise"))
                       ?? _moteur.VoixActuelle;
        _listeVoix.SelectedItem = voix;
        _moteur.VoixActuelle = voix;

        // Clamp : au cas où le fichier contiendrait une valeur hors des limites du curseur.
        _curseurVitesse.Value = Math.Clamp(_reglages.Vitesse, _curseurVitesse.Minimum, _curseurVitesse.Maximum);
        _curseurTimbre.Value = Math.Clamp(_reglages.Timbre, _curseurTimbre.Minimum, _curseurTimbre.Maximum);
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

    /// <summary>
    /// Applique les nouveaux réglages à la lecture en cours et les enregistre,
    /// après 300 ms sans changement.
    /// </summary>
    private void DemanderRelance()
    {
        _minuteurRelance.Stop();
        _minuteurRelance.Start();
    }

    private void EnregistrerReglages()
    {
        _reglages.Voix = _moteur.VoixActuelle;
        _reglages.Vitesse = _curseurVitesse.Value;
        _reglages.Timbre = _curseurTimbre.Value;
        _reglages.Enregistrer();
    }

    // Vitesse réelle de Denise pour chaque cran SAPI, de -5 à 10 (durées mesurées d'un même texte
    // rendu dans un fichier wav). SAPI n'accepte que ces crans entiers, quelle que soit la
    // façon de demander la vitesse (propriété Rate, balise <rate>, SSML <prosody>) : un
    // multiplicateur comme ×1,25 ou ×1,5 est arrondi à un de ces crans.
    private static readonly double[] MultiplicateursVitesse =
        [0.67, 0.74, 0.80, 0.87, 0.94, 1.00, 1.2, 1.4, 1.6, 1.8, 2.0, 2.2, 2.4, 2.6, 2.8, 3.0];

    /// <summary>Multiplicateur mesuré pour un cran SAPI. C'est approximatif pour les autres voix.</summary>
    private static string LibelleVitesse(int vitesse)
    {
        int index = Math.Clamp(vitesse + 5, 0, MultiplicateursVitesse.Length - 1);
        return $"≈ ×{MultiplicateursVitesse[index]:0.0#}";
    }

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
        _lectureDeLaZone = true;
        Executer(() => _moteur.Lire(texte, depart));
    }

    /// <summary>Lit un texte qui n'est pas dans la zone (le guide des voix), sans surlignage.</summary>
    private void LireTexteExterne(string texte)
    {
        _lectureDeLaZone = false;
        Executer(() => _moteur.Lire(texte));
    }

    /// <summary>
    /// Touche Espace dans la fenêtre : pause / reprise pendant une lecture.
    ///
    /// Pas de conflit avec l'écriture : pendant la lecture et la pause, la zone de texte est en
    /// lecture seule. Quand rien n'est lu, Espace garde son rôle normal (taper un espace).
    /// ProcessCmdKey est appelée avant que la touche n'atteigne le contrôle qui a le focus :
    /// renvoyer true « consomme » la touche (un bouton sélectionné ne sera donc pas cliqué).
    /// </summary>
    protected override bool ProcessCmdKey(ref Message message, Keys touches)
    {
        if (touches == Keys.Space && _moteur.EstOccupe)
        {
            Executer(_moteur.PauseOuReprise);
            return true;
        }
        return base.ProcessCmdKey(ref message, touches);
    }

    /// <summary>
    /// Clic dans le texte pendant une lecture ou une pause : on saute au mot cliqué.
    /// On ignore les glisser (sélection de texte) : la souris doit être relâchée là où elle a été enfoncée.
    /// </summary>
    private void QuandClicDansTexte(object? sender, MouseEventArgs e)
    {
        bool simpleClic = Math.Abs(e.X - _pointAppuiSouris.X) < 4 && Math.Abs(e.Y - _pointAppuiSouris.Y) < 4;
        if (e.Button != MouseButtons.Left || !simpleClic || !_moteur.EstOccupe || !_lectureDeLaZone) return;

        // Le clic vient de poser le curseur sur le mot. GetCharIndexFromPosition serait faux
        // au-delà du 65 535e caractère (message Windows limité à 16 bits).
        int position = _zoneTexte.SelectionStart;
        Executer(() => _moteur.AllerA(position));
        _dernierMotSurligne = null;   // force le surlignage du nouveau mot
    }

    /// <summary>
    /// Raccourci de lecture pressé (n'importe où dans Windows) :
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
                // Fenêtre cachée : le message serait invisible, on prévient par un petit son.
                if (!Visible) System.Media.SystemSounds.Asterisk.Play();
                return;
            }
            // Le texte capturé s'affiche dans la fenêtre : on peut le relire ou le modifier ensuite.
            _zoneTexte.Text = texte;
            _lectureDeLaZone = true;
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
            if (_lectureDeLaZone) SurlignerMotEnCours();
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
        else if (_moteur.EnPause) etat = "En pause. Espace = reprendre ; clic sur un mot = reprendre à cet endroit.";
        else if (occupe) etat = $"Lecture en cours…  Espace = pause ; clic sur un mot = y sauter ; {NomRaccourci} = arrêter.";
        else if (_raccourci == null) etat = "Aucune touche de lecture active : menu Configuration → Touche de lecture.";
        else etat = $"Sélectionne du texte n'importe où et appuie sur {NomRaccourci} pour l'écouter.";

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

    // ------------------------------------------------------------------ Raccourci de lecture

    /// <summary>
    /// Réserve le raccourci auprès de Windows (en libérant l'ancien).
    /// Renvoie false, avec le message d'erreur, si un autre programme l'utilise déjà.
    /// </summary>
    private bool ActiverRaccourci(Keys combinaison, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? erreur)
    {
        _raccourci?.Dispose();
        _raccourci = null;
        try
        {
            _raccourci = new RaccourciGlobal(combinaison);
            _raccourci.Appuye += QuandRaccourciAppuye;
            _iconeNotification.Text = $"Lecteur vocal — {NomsTouches.Decrire(combinaison)} pour lire la sélection";
            erreur = null;
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _iconeNotification.Text = "Lecteur vocal — aucune touche de lecture active";
            erreur = ex.Message;
            return false;
        }
    }

    private void ChoisirRaccourci()
    {
        Keys ancien = _reglages.Raccourci;
        // Le raccourci actuel est libéré pendant le choix : sinon Windows l'intercepterait
        // et la fenêtre de choix ne recevrait jamais la touche.
        _raccourci?.Dispose();
        _raccourci = null;

        // using : la fenêtre de dialogue est libérée (Dispose) à la fin du bloc.
        using var fenetre = new FenetreRaccourci(ancien);
        Keys choisi = fenetre.ShowDialog(this) == DialogResult.OK ? fenetre.Combinaison : ancien;
        if (IsDisposed) return;   // « Quitter » cliqué dans l'icône pendant le dialogue

        if (ActiverRaccourci(choisi, out string? erreur))
        {
            _reglages.Raccourci = choisi;
            _reglages.Enregistrer();
            return;
        }
        // Échec : on remet l'ancien raccourci, s'il est différent et encore libre.
        bool ancienRetabli = choisi != ancien && ActiverRaccourci(ancien, out _);
        MessageBox.Show(this,
            erreur + (ancienRetabli ? "\r\nL'ancien raccourci est conservé." : "\r\nAucune touche de lecture n'est active."),
            "Touche de lecture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ------------------------------------------------------------------ Fichiers

    private const string FiltreFichiers = "Fichiers texte (*.txt;*.md)|*.txt;*.md|Tous les fichiers (*.*)|*.*";

    private void OuvrirFichier()
    {
        using var dialogue = new OpenFileDialog { Title = "Ouvrir un fichier texte", Filter = FiltreFichiers };
        if (dialogue.ShowDialog(this) != DialogResult.OK) return;

        Executer(_moteur.Arreter);
        try
        {
            _zoneTexte.Text = LireFichierTexte(dialogue.FileName);
            _zoneTexte.Select(0, 0);   // curseur au début : Lire partira du début
            AfficherMessage($"Fichier ouvert : {Path.GetFileName(dialogue.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir le fichier :\r\n{ex.Message}", "Ouvrir",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Lit un fichier texte en devinant son encodage. Une « BOM » (marque en tête de fichier)
    /// le donne sans ambiguïté : UTF-8, ou UTF-16 (Bloc-notes, redirection « > » de PowerShell 5).
    /// Sans BOM, les fichiers récents sont en UTF-8 et les anciens fichiers Windows souvent en
    /// « Windows-1252 » : on essaie UTF-8 en mode strict, et on se rabat sur Windows-1252 si le
    /// contenu n'est pas de l'UTF-8 valide.
    /// </summary>
    private static string LireFichierTexte(string chemin)
    {
        byte[] octets = File.ReadAllBytes(chemin);
        try
        {
            // detectEncodingFromByteOrderMarks : reconnaît et retire la BOM éventuelle.
            // throwOnInvalidBytes: true → exception au premier octet qui n'est pas de l'UTF-8 valide.
            using var lecteur = new StreamReader(new MemoryStream(octets),
                new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            return lecteur.ReadToEnd().ReplaceLineEndings("\r\n");
        }
        catch (System.Text.DecoderFallbackException)
        {
            // .NET n'inclut pas les vieux encodages par défaut : on les active avant d'utiliser 1252.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(1252).GetString(octets).ReplaceLineEndings("\r\n");
        }
    }

    private void EnregistrerFichier()
    {
        using var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer le texte",
            Filter = FiltreFichiers,
            DefaultExt = "txt",
            FileName = "texte.txt",
        };
        if (dialogue.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dialogue.FileName, _zoneTexte.Text);   // UTF-8 par défaut
            AfficherMessage($"Texte enregistré : {Path.GetFileName(dialogue.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossible d'enregistrer :\r\n{ex.Message}", "Enregistrer",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ------------------------------------------------------------------ Aide

    private void AfficherAideVoix()
    {
        using var fenetre = new FenetreAideVoix(LireTexteExterne);
        fenetre.ShowDialog(this);
        // « Quitter » cliqué dans l'icône pendant le guide : la voix est déjà libérée.
        if (IsDisposed) return;
        // Fermer le guide arrête sa lecture (mais pas celle de la zone de texte).
        if (!_lectureDeLaZone) Executer(_moteur.Arreter);
    }

    private void AfficherAPropos()
    {
        MessageBox.Show(this,
            $"Lecteur vocal {Application.ProductVersion.Split('+')[0]}\r\n\r\n"
            + "Lecture à voix haute, hors ligne, du texte collé ou sélectionné.\r\n"
            + "Voix Windows via SAPI (voix naturelles grâce à NaturalVoiceSAPIAdapter).\r\n\r\n"
            + $"Raccourci de lecture : {NomRaccourci}\r\n"
            + "Espace (dans la fenêtre) : pause / reprise",
            "À propos", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ Affichage et fermeture

    /// <summary>Fait réapparaître la fenêtre (depuis l'icône, ou quand on relance le programme).</summary>
    private void Afficher()
    {
        _affichageAutorise = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void Quitter()
    {
        _quitter = true;
        Close();
    }

    /// <summary>
    /// Windows appelle SetVisibleCore pour afficher ou masquer la fenêtre, y compris au tout
    /// premier affichage lancé par Application.Run. En le surchargeant, on peut démarrer caché.
    /// </summary>
    protected override void SetVisibleCore(bool visible)
    {
        base.SetVisibleCore(visible && _affichageAutorise);
    }

    /// <summary>
    /// Clic sur la croix : on cache la fenêtre au lieu de quitter, pour que la touche Pause
    /// continue de fonctionner. On quitte vraiment via « Quitter » dans le menu de l'icône,
    /// ou quand Windows s'éteint.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_quitter)
        {
            e.Cancel = true;
            Hide();
            if (!_reglages.ReductionDejaExpliquee)
            {
                _iconeNotification.ShowBalloonTip(6000, "Le Lecteur vocal reste actif",
                    $"La touche {NomRaccourci} fonctionne toujours. Clic sur cette icône pour rouvrir la fenêtre, "
                    + "clic droit → Quitter pour l'arrêter.", ToolTipIcon.Info);
                _reglages.ReductionDejaExpliquee = true;
                _reglages.Enregistrer();
            }
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _minuteur.Stop();
        _minuteurRelance.Stop();
        _attenteSignal.Unregister(null);
        EnregistrerReglages();
        // Sans ça, l'icône resterait affichée près de l'horloge jusqu'au survol de la souris.
        _iconeNotification.Visible = false;
        _iconeNotification.Dispose();
        _raccourci?.Dispose();
        _moteur.Dispose();
        base.OnFormClosed(e);
    }
}
