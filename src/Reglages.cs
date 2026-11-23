using System.Text.Json;
using System.Text.Json.Serialization;

namespace LecteurVocal;

/// <summary>
/// Les réglages mémorisés d'une utilisation à l'autre, enregistrés dans un petit fichier JSON :
/// %AppData%\LecteurVocal\reglages.json (AppData = dossier caché propre à chaque utilisateur,
/// prévu par Windows pour ce genre de fichiers).
/// </summary>
public sealed class Reglages
{
    public string? Voix { get; set; }
    public int Vitesse { get; set; } = 3;   // ≈ ×1,6
    public int Timbre { get; set; }

    /// <summary>
    /// Raccourci de lecture de la sélection. Enregistré en texte (« L, Control, Alt »)
    /// plutôt qu'en nombre, pour rester lisible dans le fichier JSON.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Keys>))]
    public Keys Raccourci { get; set; } = RaccourciParDefaut;

    /// <summary>
    /// Ctrl + Maj + la touche « &lt; &gt; » (à gauche de W sur un clavier AZERTY). C'est une seule
    /// touche physique : on enregistre la touche, pas le caractère, donc Maj ne change rien.
    /// </summary>
    public const Keys RaccourciParDefaut = Keys.Control | Keys.Shift | Keys.OemBackslash;

    /// <summary>Vrai une fois qu'on a expliqué que fermer la fenêtre laisse l'appli active.</summary>
    public bool ReductionDejaExpliquee { get; set; }

    private static readonly string Chemin = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LecteurVocal", "reglages.json");

    // WriteIndented : fichier lisible par un humain (une propriété par ligne).
    private static readonly JsonSerializerOptions OptionsJson = new() { WriteIndented = true };

    /// <summary>Lit les réglages ; renvoie les valeurs par défaut si le fichier n'existe pas ou est abîmé.</summary>
    public static Reglages Charger()
    {
        try
        {
            if (File.Exists(Chemin))
                return JsonSerializer.Deserialize<Reglages>(File.ReadAllText(Chemin)) ?? new Reglages();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fichier illisible : on repart des valeurs par défaut plutôt que de bloquer le lancement.
        }
        return new Reglages();
    }

    public void Enregistrer()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Chemin)!);
            File.WriteAllText(Chemin, JsonSerializer.Serialize(this, OptionsJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Impossible d'écrire (disque plein...) : les réglages ne seront simplement pas mémorisés.
        }
    }
}
