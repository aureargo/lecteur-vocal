using System.Runtime.InteropServices;
using System.Text;

namespace LecteurVocal;

/// <summary>
/// Pilote SAPI 5, le moteur de synthèse vocale intégré à Windows.
///
/// Pourquoi SAPI et pas l'API .NET « System.Speech » ? System.Speech ne voit que les voix
/// inscrites « en dur » dans le registre (Hortense...). Denise et Henri sont fournies
/// dynamiquement par NaturalVoiceSAPIAdapter : seul SAPI lui-même sait les énumérer.
///
/// SAPI est un composant COM (la vieille technologie de Windows pour partager des objets
/// entre programmes). On le pilote ici avec le mot-clé C# <c>dynamic</c> : les appels
/// de méthodes sont résolus à l'exécution, comme en JavaScript, au lieu d'être vérifiés
/// à la compilation. C'est la façon la plus simple de parler à COM sans fichier d'interop.
/// </summary>
public sealed class MoteurVocal : IDisposable
{
    // Drapeaux de la méthode Speak (énumération SpeechVoiceSpeakFlags de la doc SAPI).
    // Ce sont des bits qu'on combine avec l'opérateur | (OU binaire).
    private const int ParlerEnArrierePlan = 1;   // SVSFlagsAsync : Speak rend la main tout de suite
    private const int ViderFileAttente = 2;      // SVSFPurgeBeforeSpeak : coupe la lecture en cours
    private const int TexteEnXml = 8;            // SVSFIsXML : le texte contient des balises SAPI

    // Valeur de Status.RunningState quand la voix a fini de parler (SRSEDone).
    private const int EtatTermine = 1;

    private readonly dynamic _voix;                                   // l'objet COM « SAPI.SpVoice »
    private readonly Dictionary<string, dynamic> _jetonsParNom = new(); // nom affiché → jeton de voix

    // Le texte envoyé à SAPI n'est pas exactement celui de l'utilisateur (balises ajoutées,
    // caractères « & < > » échappés). Ces deux champs permettent de retrouver, à partir d'une
    // position dans le texte envoyé, la position dans le texte d'origine.
    private int _longueurPrefixe;
    private int[] _versOriginal = [];

    public MoteurVocal()
    {
        // GetTypeFromProgID cherche dans le registre l'objet COM nommé « SAPI.SpVoice ».
        Type type = Type.GetTypeFromProgID("SAPI.SpVoice")
                    ?? throw new InvalidOperationException("La synthèse vocale SAPI est introuvable sur ce PC.");
        _voix = Activator.CreateInstance(type)!;

        // GetVoices renvoie une collection COM : on la parcourt par index (Item), ce qui est
        // plus fiable qu'un foreach sur un objet dynamic.
        dynamic jetons = _voix.GetVoices();
        int nombre = jetons.Count;
        for (int i = 0; i < nombre; i++)
        {
            dynamic jeton = jetons.Item(i);
            _jetonsParNom[(string)jeton.GetDescription()] = jeton;
        }
    }

    /// <summary>Noms des voix installées, triés alphabétiquement.</summary>
    public IReadOnlyList<string> Voix => _jetonsParNom.Keys.Order().ToList();

    /// <summary>Nom de la voix utilisée. Un nom inconnu est ignoré.</summary>
    public string VoixActuelle
    {
        get => (string)_voix.Voice.GetDescription();
        set
        {
            if (_jetonsParNom.TryGetValue(value, out dynamic? jeton))
                _voix.Voice = jeton;
        }
    }

    /// <summary>Vitesse SAPI, de -10 (lent) à 10 (rapide). 0 = vitesse normale.</summary>
    public int Vitesse
    {
        get => (int)_voix.Rate;
        set => _voix.Rate = Math.Clamp(value, -10, 10);
    }

    /// <summary>Hauteur de la voix (timbre), de -10 (grave) à 10 (aigu).</summary>
    public int Timbre { get; set; }

    /// <summary>Vrai tant que la voix parle (y compris quand elle est en pause).</summary>
    public bool EstOccupe => (int)_voix.Status.RunningState != EtatTermine;

    public bool EnPause { get; private set; }

    /// <summary>Lance la lecture du texte (et coupe la lecture précédente s'il y en a une).</summary>
    public void Lire(string texte)
    {
        Arreter();

        // Le timbre ne se règle pas par une propriété : il passe par une balise XML SAPI
        // qui entoure le texte. Le texte doit donc être « échappé » pour rester du XML valide.
        string prefixe = $"<pitch absmiddle=\"{Math.Clamp(Timbre, -10, 10)}\">";
        (string corps, _versOriginal) = EchapperPourXml(texte);
        _longueurPrefixe = prefixe.Length;

        _voix.Speak(prefixe + corps + "</pitch>", ParlerEnArrierePlan | ViderFileAttente | TexteEnXml);
    }

    /// <summary>Met en pause, ou reprend si on était déjà en pause.</summary>
    public void PauseOuReprise()
    {
        if (!EstOccupe) return;
        if (EnPause) _voix.Resume(); else _voix.Pause();
        EnPause = !EnPause;
    }

    public void Arreter()
    {
        // Une voix en pause doit être relancée, sinon elle resterait bloquée
        // et la lecture suivante ne démarrerait jamais.
        if (EnPause)
        {
            _voix.Resume();
            EnPause = false;
        }
        // Parler une chaîne vide en vidant la file = arrêter net.
        _voix.Speak("", ParlerEnArrierePlan | ViderFileAttente);
    }

    /// <summary>
    /// Position du mot en train d'être prononcé, dans le texte d'origine.
    /// Renvoie null si aucune lecture n'est en cours ou si la position est inconnue.
    /// </summary>
    public (int Debut, int Longueur)? MotEnCours()
    {
        if (!EstOccupe) return null;

        dynamic statut = _voix.Status;
        int debut = (int)statut.InputWordPosition - _longueurPrefixe;
        int longueur = (int)statut.InputWordLength;
        if (debut < 0 || longueur <= 0 || debut >= _versOriginal.Length) return null;

        int fin = Math.Min(debut + longueur, _versOriginal.Length) - 1;
        int debutOriginal = _versOriginal[debut];
        return (debutOriginal, _versOriginal[fin] - debutOriginal + 1);
    }

    /// <summary>
    /// Remplace les caractères spéciaux du XML (&amp; &lt; &gt;) par leurs codes, et note pour
    /// chaque caractère produit l'index du caractère d'origine correspondant.
    /// </summary>
    private static (string Texte, int[] VersOriginal) EchapperPourXml(string texte)
    {
        var sortie = new StringBuilder(texte.Length);
        var correspondance = new List<int>(texte.Length);

        for (int i = 0; i < texte.Length; i++)
        {
            char c = texte[i];
            string remplacement = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                // Les caractères de contrôle (hors tabulation et retours à la ligne) sont
                // interdits en XML : SAPI refuserait tout le texte. On les remplace par un espace.
                _ when char.IsControl(c) && c is not ('\t' or '\n' or '\r') => " ",
                _ => c.ToString(),
            };
            sortie.Append(remplacement);
            for (int k = 0; k < remplacement.Length; k++) correspondance.Add(i);
        }
        return (sortie.ToString(), correspondance.ToArray());
    }

    public void Dispose()
    {
        try { Arreter(); } catch (COMException) { /* on ferme de toute façon */ }
        // Libère l'objet COM tout de suite au lieu d'attendre le ramasse-miettes.
        Marshal.FinalReleaseComObject((object)_voix);
    }
}
