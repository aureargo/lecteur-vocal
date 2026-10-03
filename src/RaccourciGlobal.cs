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
    private const int Identifiant = 1;

    /// <summary>Déclenché à chaque appui sur le raccourci.</summary>
    public event Action? Appuye;

    /// <exception cref="Win32Exception">Si la touche est déjà prise par un autre programme.</exception>
    public RaccourciGlobal(Keys touche)
    {
        CreateHandle(new CreateParams());   // crée la fenêtre invisible

        if (!RegisterHotKey(Handle, Identifiant, MOD_NOREPEAT, (uint)touche))
        {
            int erreur = Marshal.GetLastWin32Error();
            DestroyHandle();
            throw new Win32Exception(erreur, $"La touche {touche} est déjà utilisée par un autre programme.");
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
