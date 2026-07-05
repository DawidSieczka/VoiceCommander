namespace VoiceTypePL.Vad;

/// <summary>
/// Model VAD oceniający pojedyncze okno audio. Abstrakcja pozwala odseparować maszynę stanów
/// segmentacji (<see cref="VadSegmenter"/>) od konkretnej sieci — w testach wstrzykujemy atrapę
/// ze skryptem prawdopodobieństw, w produkcji <see cref="SileroVadModel"/>.
/// </summary>
public interface IVadModel : IDisposable
{
    /// <summary>Liczba próbek, jaką model oczekuje w jednym oknie (Silero v5 @16 kHz = 512).</summary>
    int WindowSize { get; }

    /// <summary>Zwraca prawdopodobieństwo mowy [0, 1] dla okna audio (16 kHz mono, [-1, 1]).</summary>
    float Process(ReadOnlySpan<float> window);

    /// <summary>Zeruje wewnętrzny stan rekurencyjny (na początku nowego strumienia).</summary>
    void Reset();
}
