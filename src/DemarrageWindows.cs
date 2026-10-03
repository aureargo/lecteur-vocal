using Microsoft.Win32;

namespace LecteurVocal;

/// <summary>
/// Lancement automatique à l'ouverture de la session Windows.
///
/// Mécanisme : Windows lance au démarrage tous les programmes listés dans la clé de registre
/// HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run (une valeur = un programme).
/// « CURRENT_USER » : ça ne concerne que ton compte, pas besoin des droits administrateur.
/// </summary>
public static class DemarrageWindows
{
    private const string CleRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NomValeur = "LecteurVocal";

    /// <summary>Argument ajouté à la commande : au démarrage, l'appli se lance cachée dans la zone de notification.</summary>
    public const string ArgumentDemarrage = "--demarrage";

    public static bool EstActive
    {
        get
        {
            using RegistryKey? cle = Registry.CurrentUser.OpenSubKey(CleRun);
            return cle?.GetValue(NomValeur) != null;
        }
    }

    public static void Activer(bool actif)
    {
        using RegistryKey cle = Registry.CurrentUser.CreateSubKey(CleRun);
        if (actif)
            // Guillemets autour du chemin : indispensables s'il contient des espaces.
            cle.SetValue(NomValeur, $"\"{Environment.ProcessPath}\" {ArgumentDemarrage}");
        else
            cle.DeleteValue(NomValeur, throwOnMissingValue: false);
    }
}
