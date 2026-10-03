namespace LecteurVocal;

/// <summary>
/// Petite fenêtre où l'on appuie sur la touche ou la combinaison voulue pour lire la sélection.
/// S'ouvre avec ShowDialog : la fenêtre principale est bloquée tant qu'elle est ouverte.
/// </summary>
public sealed class FenetreRaccourci : Form
{
    private readonly TextBox _saisie = new();
    private readonly Label _avertissement = new();
    private readonly Button _boutonOk = new();

    /// <summary>La combinaison choisie (valable si la fenêtre se ferme avec OK).</summary>
    public Keys Combinaison { get; private set; }

    public FenetreRaccourci(Keys actuelle)
    {
        Text = "Touche de lecture";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var consigne = new Label
        {
            Text = "Appuie sur la touche ou la combinaison voulue\r\n(par exemple Pause, ou Ctrl + Alt + L).",
            AutoSize = true,
        };

        _saisie.ReadOnly = true;
        _saisie.Width = 380;
        _saisie.Font = new Font("Segoe UI", 14f);
        _saisie.TextAlign = HorizontalAlignment.Center;
        _saisie.BackColor = SystemColors.Window;
        // Par défaut, Tab, les flèches ou Entrée servent à naviguer dans la fenêtre.
        // IsInputKey = true : on veut les recevoir comme des touches normales.
        _saisie.PreviewKeyDown += (_, e) => e.IsInputKey = true;
        _saisie.KeyDown += QuandToucheAppuyee;

        _avertissement.AutoSize = true;
        _avertissement.MaximumSize = new Size(380, 0);   // retour à la ligne automatique
        _avertissement.ForeColor = Color.Firebrick;

        var boutonPause = new Button { Text = "Remettre « Pause »", AutoSize = true };
        boutonPause.Click += (_, _) => Proposer(Keys.Pause);
        var boutonAnnuler = new Button { Text = "Annuler", AutoSize = true, DialogResult = DialogResult.Cancel };
        _boutonOk.Text = "OK";
        _boutonOk.AutoSize = true;
        _boutonOk.DialogResult = DialogResult.OK;   // cliquer ferme la fenêtre avec ce résultat

        var boutons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        boutons.Controls.AddRange([_boutonOk, boutonAnnuler, boutonPause]);

        var disposition = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        disposition.Controls.AddRange([consigne, _saisie, _avertissement, boutons]);
        Controls.Add(disposition);

        Proposer(actuelle);
    }

    private void QuandToucheAppuyee(object? sender, KeyEventArgs e)
    {
        // La touche ne doit rien faire d'autre (pas de bip, pas de menu avec Alt).
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }

        // Seulement Ctrl, Alt ou Maj enfoncés pour l'instant : on affiche « Ctrl + … » en attendant la suite.
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
        {
            _saisie.Text = NomsTouches.Decrire(e.Modifiers) + " + …";
            return;
        }
        Proposer(e.KeyData);
    }

    private void Proposer(Keys combinaison)
    {
        _saisie.Text = NomsTouches.Decrire(combinaison);
        bool acceptable = NomsTouches.EstAcceptable(combinaison);
        _boutonOk.Enabled = acceptable;
        _avertissement.Text = acceptable
            ? ""
            : "Ajoute Ctrl ou Alt : sans eux, seules Pause, Arrêt défil et F1 à F24 sont permises "
              + "(une lettre seule empêcherait d'écrire).";
        if (acceptable) Combinaison = combinaison;
    }
}
