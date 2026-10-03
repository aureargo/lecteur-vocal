using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LecteurVocal;

/// <summary>
/// Un raccourci clavier « global » : il fonctionne même quand une autre application
/// est au premier plan (navigateur, PDF, Word...).
///
/// Mécanisme Windows : on demande au système, via la fonction RegisterHotKey,
/// de nous prévenir quand la touche est pressée. Windows envoie alors un message
/// WM_HOTKEY à une fenêtre qui nous appartient. Ici, cette fenêtre est invisible
/// (une <see cref="NativeWindow"/> sans affichage) : elle sert uniquement de boîte aux lettres.
/// On n'utilise pas la fenêtre principale car Windows la recrée parfois (passage dans la
/// zone de notification...), et le raccourci attaché à l'ancienne serait perdu.
/// </summary>
public sealed class RaccourciGlobal : NativeWindow, IDisposable
{
    // [DllImport] déclare une fonction écrite en C dans une DLL de Windows, pour pouvoir
    // l'appeler depuis C# (mécanisme appelé « P/Invoke »).
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr fenetre, int identifiant, uint modificateurs, uint touche);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr fenetre, int identifiant);

    private const int WM_HOTKEY = 0x0312;
    // MOD_NOREPEAT : garder la touche enfoncée ne déclenche le raccourci qu'une fois.
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const int Identifiant = 1;

    /// <summary>Déclenché à chaque appui sur le raccourci.</summary>
    public event Action? Appuye;

    /// <param name="combinaison">
    /// Touche principale + modificateurs éventuels, combinés comme dans WinForms :
    /// par exemple <c>Keys.Control | Keys.Alt | Keys.L</c>.
    /// </param>
    /// <exception cref="Win32Exception">Si la combinaison est déjà prise par un autre programme.</exception>
    public RaccourciGlobal(Keys combinaison)
    {
        CreateHandle(new CreateParams());   // crée la fenêtre invisible

        // Dans l'énumération Keys, les modificateurs sont des bits « hauts » ajoutés au code de la touche.
        // RegisterHotKey les veut séparés : on traduit chaque bit en drapeau MOD_xxx.
        uint modificateurs = MOD_NOREPEAT;
        if (combinaison.HasFlag(Keys.Control)) modificateurs |= MOD_CONTROL;
        if (combinaison.HasFlag(Keys.Alt)) modificateurs |= MOD_ALT;
        if (combinaison.HasFlag(Keys.Shift)) modificateurs |= MOD_SHIFT;
        Keys touche = combinaison & Keys.KeyCode;   // KeyCode = masque qui garde seulement la touche

        if (!RegisterHotKey(Handle, Identifiant, modificateurs, (uint)touche))
        {
            int erreur = Marshal.GetLastWin32Error();
            DestroyHandle();
            throw new Win32Exception(erreur,
                $"La combinaison {NomsTouches.Decrire(combinaison)} est déjà utilisée par un autre programme.");
        }
    }

    // WndProc reçoit tous les messages que Windows envoie à la fenêtre.
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WM_HOTKEY)
            Appuye?.Invoke();
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, Identifiant);
        DestroyHandle();
    }
}
