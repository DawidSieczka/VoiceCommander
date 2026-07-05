namespace VoiceTypePL.Core.Audio;

/// <summary>
/// Źródło strumienia audio w formacie <see cref="AudioFormat"/> (16 kHz mono), emitujące ramki.
/// Abstrakcja pozwala podmienić realne przechwytywanie z mikrofonu (WASAPI) na źródło z pliku WAV
/// (testy, weryfikacja headless) bez zmian w VAD i reszcie pipeline'u.
/// </summary>
public interface IAudioCaptureSource : IDisposable
{
    /// <summary>Zgłaszane dla każdej gotowej ramki audio (16 kHz mono).</summary>
    event EventHandler<AudioFrame>? FrameReady;

    /// <summary>
    /// Zgłaszane, gdy strumień się kończy (zatrzymanie mikrofonu lub koniec pliku).
    /// Pozwala domknąć ostatni otwarty segment mowy (Flush segmentera).
    /// </summary>
    event EventHandler? CaptureStopped;

    /// <summary>Czy źródło aktualnie produkuje ramki.</summary>
    bool IsCapturing { get; }

    /// <summary>Rozpoczyna przechwytywanie/odtwarzanie strumienia.</summary>
    void Start();

    /// <summary>Zatrzymuje przechwytywanie.</summary>
    void Stop();
}
