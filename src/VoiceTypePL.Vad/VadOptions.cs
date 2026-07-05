namespace VoiceTypePL.Vad;

/// <summary>
/// Parametry segmentacji VAD (§5.2). Na tym etapie wartości domyślne w kodzie — przeniesienie
/// do pliku konfiguracji (regulacja przez użytkownika) zaplanowane na Etap 7 „Szlif".
/// </summary>
public sealed class VadOptions
{
    /// <summary>Próg prawdopodobieństwa, powyżej którego okno uznajemy za początek/mowę.</summary>
    public float SpeechThreshold { get; set; } = 0.5f;

    /// <summary>Dolny próg histerezy — poniżej niego okno liczymy jako ciszę (redukuje migotanie).</summary>
    public float SilenceThreshold { get; set; } = 0.35f;

    /// <summary>Cisza kończąca zdanie (§5.2: 400–1200 ms, domyślnie 700 ms).</summary>
    public TimeSpan MinSilenceDuration { get; set; } = TimeSpan.FromMilliseconds(700);

    /// <summary>Minimalna długość mowy w segmencie — krótsze odrzucamy (stuki, klik).</summary>
    public TimeSpan MinSpeechDuration { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Maksymalna długość segmentu — po jej przekroczeniu segment jest domykany na siłę.</summary>
    public TimeSpan MaxSpeechDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Padding ciszy dodawany przed i po mowie (§5.2: 250 ms).</summary>
    public TimeSpan SpeechPadding { get; set; } = TimeSpan.FromMilliseconds(250);
}
