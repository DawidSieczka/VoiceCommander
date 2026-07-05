namespace VoiceTypePL.Core.Speech;

/// <summary>
/// Wynik transkrypcji jednego segmentu mowy (§5.3) — „zdanie" gotowe do pokazania w dymku (Etap 3)
/// i wpisania (Etap 4). Tekst jest już po post-processingu (trim, kapitalizacja, interpunkcja).
/// </summary>
public sealed class TranscribedSentence
{
    public TranscribedSentence(
        string text,
        TimeSpan audioDuration,
        TimeSpan processingDuration,
        DateTimeOffset transcribedAt)
    {
        Text = text;
        AudioDuration = audioDuration;
        ProcessingDuration = processingDuration;
        TranscribedAt = transcribedAt;
    }

    /// <summary>Przetranskrybowany i uporządkowany tekst zdania.</summary>
    public string Text { get; }

    /// <summary>Długość nagrania, z którego powstała transkrypcja.</summary>
    public TimeSpan AudioDuration { get; }

    /// <summary>Czas samej transkrypcji (latencja) — do weryfikacji kryterium §7 (&lt; 3 s GPU / &lt; 8 s CPU).</summary>
    public TimeSpan ProcessingDuration { get; }

    /// <summary>Moment zakończenia transkrypcji.</summary>
    public DateTimeOffset TranscribedAt { get; }
}
