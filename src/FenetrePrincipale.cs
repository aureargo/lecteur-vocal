namespace LecteurVocal;

/// <summary>
/// La fenêtre de l'application. Pour l'instant vide : on vérifie juste que tout compile.
/// </summary>
public sealed class FenetrePrincipale : Form
{
    public FenetrePrincipale()
    {
        Text = "Lecteur vocal";
        Size = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
    }
}
