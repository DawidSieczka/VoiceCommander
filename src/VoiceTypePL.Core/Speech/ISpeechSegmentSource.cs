namespace VoiceTypePL.Core.Speech;

/// <summary>
/// Źródło gotowych segmentów mowy (produkuje je <c>VadSegmenter</c>).
/// Kolejne etapy (Transcriber) subskrybują to zdarzenie zamiast znać VAD bezpośrednio —
/// wewnętrzny „event bus" z §2, dzięki któremu moduły są wymienne.
/// </summary>
public interface ISpeechSegmentSource
{
    /// <summary>Zgłaszane po domknięciu segmentu mowy (koniec zdania).</summary>
    event EventHandler<SpeechSegment>? SpeechSegmentReady;
}
