namespace VoiceTypePL.Stt;

/// <summary>
/// Parametry post-processingu transkrypcji (§5.3). Progi filtra halucynacji celowo konserwatywne —
/// VAD już odcina większość ciszy, więc filtr ma łapać tylko wyraźne artefakty.
/// </summary>
public sealed class TranscriptPostProcessorOptions
{
    /// <summary>
    /// Powyżej tego prawdopodobieństwa „braku mowy" (no_speech_prob z Whispera) segment odrzucamy
    /// jako halucynację na ciszy/szumie.
    /// </summary>
    public float MaxNoSpeechProbability { get; set; } = 0.6f;

    /// <summary>
    /// Poniżej tej średniej pewności tokenów (Whisper <c>Probability</c>, ~exp(avg_logprob)) segment
    /// uznajemy za niepewny i odrzucamy. 0 wyłącza próg.
    /// </summary>
    public float MinAvgProbability { get; set; } = 0.30f;

    /// <summary>Znane artefakty/halucynacje Whispera dla polskiego (napisy, stopki lektorskie itp.).</summary>
    public IReadOnlyList<string> HallucinationPhrases { get; set; } = DefaultHallucinationPhrases;

    /// <summary>
    /// Domyślna lista fraz-halucynacji. Dopasowanie jest bez rozróżniania wielkości liter i znaków
    /// diakrytycznych; frazę uznajemy za trafioną, gdy cała (krótka) transkrypcja ją zawiera.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultHallucinationPhrases = new[]
    {
        "napisy stworzone przez spolecznosc amara.org",
        "napisy: amara.org",
        "subtitles by the amara.org community",
        "dziekuje za uwage",
        "dziekuje za obejrzenie",
        "dziekuje za ogladanie",
        "zapraszam do subskrypcji",
        "zapraszam na kolejny odcinek",
        "do zobaczenia w kolejnym odcinku",
        "napisy stworzone przez",
        "transkrypcja: ",
        "prezes: ",
    };
}
