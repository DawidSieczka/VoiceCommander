namespace VoiceTypePL.Core.Speech;

/// <summary>
/// Backend transkrypcji mowy na tekst (§5.3). Abstrakcja z §2/§10: dzięki niej Whisper.net można
/// w przyszłości podmienić (np. na Azure) bez ruszania reszty pipeline'u. Pełni jednocześnie rolę
/// wejścia (kolejkowanie segmentów przez <see cref="Enqueue"/>) i źródła zdarzeń event-busa
/// (<see cref="SentenceTranscribed"/>).
///
/// Cykl życia: <see cref="InitializeAsync"/> (załadowanie/pobranie modelu) → wielokrotny
/// <see cref="Enqueue"/> → <see cref="CompleteAsync"/> przy zamykaniu (opróżnienie kolejki).
/// Segmenty można kolejkować także przed zakończeniem inicjalizacji — czekają w kolejce.
/// </summary>
public interface ITranscriber : IAsyncDisposable
{
    /// <summary>Zgłaszane po przetranskrybowaniu i uporządkowaniu jednego segmentu mowy.</summary>
    event EventHandler<TranscribedSentence>? SentenceTranscribed;

    /// <summary>Przygotowuje backend (model w pamięci, natywki runtime). Idempotentne.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Dodaje segment mowy do kolejki transkrypcji (nie blokuje).</summary>
    void Enqueue(SpeechSegment segment);

    /// <summary>Sygnalizuje koniec dopływu segmentów i czeka na opróżnienie kolejki.</summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
