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
///
/// Particularité importante : avec les voix naturelles, SAPI n'applique la vitesse et le
/// timbre qu'au lancement d'une lecture, et sa propre pause saute un bout de texte.
/// Le moteur gère donc lui-même la position dans le texte : mettre en pause, changer un
/// réglage ou sauter à un mot revient toujours à <b>relancer la lecture à partir d'une position</b>.
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
    private string? _voixAppliquee;   // dernière voix réellement transmise à SAPI

    // Le texte complet en cours de lecture, et la position où la dernière relance a commencé.
    private string _texte = "";
    private int _depart;

    // Le texte envoyé à SAPI n'est pas exactement celui de l'utilisateur (balises ajoutées,
    // caractères « & < > » échappés). Ces deux champs permettent de retrouver, à partir d'une
    // position dans le texte envoyé, la position dans le texte d'origine.
    private int _longueurPrefixe;
    private int[] _versOriginal = [];

    // Position (dans _texte) d'où repartira la lecture après une pause. null = pas en pause.
    private int? _positionReprise;

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

        VoixActuelle = (string)_voix.Voice.GetDescription();
        _voixAppliquee = VoixActuelle;
    }

    // ------------------------------------------------------------------ Réglages
    // Ils sont seulement mémorisés ici, puis transmis à SAPI au prochain lancement de lecture
    // (voir Parler). Pour les appliquer pendant une lecture, appeler Relancer().

    /// <summary>Noms des voix installées, triés alphabétiquement.</summary>
    public IReadOnlyList<string> Voix => _jetonsParNom.Keys.Order().ToList();

    /// <summary>Nom de la voix choisie.</summary>
    public string VoixActuelle { get; set; }

    /// <summary>Vitesse SAPI, de -10 (lent) à 10 (rapide). 0 = vitesse normale.</summary>
    public int Vitesse { get; set; }

    /// <summary>Hauteur de la voix (timbre), de -10 (grave) à 10 (aigu).</summary>
    public int Timbre { get; set; }

    // ------------------------------------------------------------------ État

    /// <summary>Vrai pendant que la voix parle (pas en pause).</summary>
    public bool EnLecture => _positionReprise == null && (int)_voix.Status.RunningState != EtatTermine;

    public bool EnPause => _positionReprise != null;

    /// <summary>Vrai si une lecture est en cours ou en pause.</summary>
    public bool EstOccupe => EnPause || EnLecture;

    // ------------------------------------------------------------------ Commandes

    /// <summary>Lit le texte à partir de la position indiquée (0 = depuis le début).</summary>
    public void Lire(string texte, int depart = 0)
    {
        _texte = texte;
        _positionReprise = null;
        Parler(Math.Clamp(depart, 0, texte.Length));
    }

    /// <summary>Met en pause en retenant le mot en cours.</summary>
    public void Pause()
    {
        if (!EnLecture) return;
        int motEnCours = MotEnCours()?.Debut ?? _depart;
        CouperSapi();
        // À la reprise, on repart du mot d'avant : la phrase redémarre avec un peu de contexte.
        _positionReprise = DebutDuMotPrecedent(_texte, motEnCours);
    }

    public void Reprendre()
    {
        if (_positionReprise is int position)
        {
            _positionReprise = null;
            Parler(position);
        }
    }

    public void PauseOuReprise()
    {
        if (EnPause) Reprendre(); else Pause();
    }

    /// <summary>
    /// Saute au mot situé à cette position du texte. En lecture, la voix y va tout de suite ;
    /// en pause, c'est de là que repartira la reprise.
    /// </summary>
    public void AllerA(int position)
    {
        int debut = DebutDuMot(_texte, position);
        if (EnPause) _positionReprise = debut;
        else if (EnLecture) Parler(debut);
    }

    /// <summary>Relance la lecture depuis le mot en cours, pour appliquer de nouveaux réglages.</summary>
    public void Relancer()
    {
        if (EnLecture) Parler(MotEnCours()?.Debut ?? _depart);
        // En pause : rien à faire, la reprise utilisera déjà les nouveaux réglages.
    }

    public void Arreter()
    {
        _positionReprise = null;
        CouperSapi();
    }

    /// <summary>
    /// Mot en cours de lecture, ou mot de reprise si on est en pause, en position dans le texte
    /// complet. null si rien n'est en cours ou si la position est inconnue.
    /// </summary>
    public (int Debut, int Longueur)? MotEnCours()
    {
        if (_positionReprise is int reprise)
            return (reprise, LongueurDuMot(_texte, reprise));
        if (!EnLecture) return null;

        dynamic statut = _voix.Status;
        int debut = (int)statut.InputWordPosition - _longueurPrefixe;
        int longueur = (int)statut.InputWordLength;
        if (debut < 0 || longueur <= 0 || debut >= _versOriginal.Length) return null;

        int fin = Math.Min(debut + longueur, _versOriginal.Length) - 1;
        int debutOriginal = _versOriginal[debut];
        return (_depart + debutOriginal, _versOriginal[fin] - debutOriginal + 1);
    }

    // ------------------------------------------------------------------ Mécanique interne

    /// <summary>Envoie à SAPI le texte à partir de <paramref name="depart"/>, avec les réglages actuels.</summary>
    private void Parler(int depart)
    {
        CouperSapi();

        // Les réglages sont transmis maintenant, voix à l'arrêt : c'est le seul moment
        // où SAPI les prend en compte de façon fiable.
        if (VoixActuelle != _voixAppliquee && _jetonsParNom.TryGetValue(VoixActuelle, out dynamic? jeton))
        {
            _voix.Voice = jeton;
            _voixAppliquee = VoixActuelle;
        }
        _voix.Rate = Math.Clamp(Vitesse, -10, 10);

        _depart = depart;
        // Le timbre ne se règle pas par une propriété : il passe par une balise XML SAPI
        // qui entoure le texte. Le texte doit donc être « échappé » pour rester du XML valide.
        string prefixe = $"<pitch absmiddle=\"{Math.Clamp(Timbre, -10, 10)}\">";
        (string corps, _versOriginal) = EchapperPourXml(_texte[depart..]);
        _longueurPrefixe = prefixe.Length;

        _voix.Speak(prefixe + corps + "</pitch>", ParlerEnArrierePlan | ViderFileAttente | TexteEnXml);
    }

    /// <summary>Arrête net la voix : parler une chaîne vide en vidant la file d'attente.</summary>
    private void CouperSapi() => _voix.Speak("", ParlerEnArrierePlan | ViderFileAttente);

    // Un « mot » = une suite de caractères sans espace (la ponctuation collée en fait partie).

    private static int DebutDuMot(string texte, int position)
    {
        position = Math.Clamp(position, 0, texte.Length);
        while (position > 0 && !char.IsWhiteSpace(texte[position - 1])) position--;
        return position;
    }

    /// <summary>Début du mot qui précède celui commençant en <paramref name="debutMot"/> (ou 0 s'il n'y en a pas).</summary>
    private static int DebutDuMotPrecedent(string texte, int debutMot)
    {
        int position = debutMot;
        while (position > 0 && char.IsWhiteSpace(texte[position - 1])) position--;   // saute les espaces
        return DebutDuMot(texte, position);
    }

    private static int LongueurDuMot(string texte, int debut)
    {
        int fin = debut;
        while (fin < texte.Length && !char.IsWhiteSpace(texte[fin])) fin++;
        return fin - debut;
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
