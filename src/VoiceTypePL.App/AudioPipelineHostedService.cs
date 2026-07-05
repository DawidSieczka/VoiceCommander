using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VoiceTypePL.Audio;
using VoiceTypePL.Core.Audio;
using VoiceTypePL.Core.Speech;
using VoiceTypePL.Vad;

namespace VoiceTypePL.App;

/// <summary>
/// Spina pipeline Etapu 1: źródło audio → <see cref="VadSegmenter"/> → log segmentów mowy.
/// Respektuje pauzę (nie karmi VAD, a wejście w pauzę domyka otwarty segment). W trybie pliku
/// (weryfikacja headless) po przetworzeniu całości zamyka aplikację.
/// </summary>
public sealed class AudioPipelineHostedService : IHostedService
{
    private readonly IAudioCaptureSource _source;
    private readonly VadSegmenter _segmenter;
    private readonly AppState _state;
    private readonly ILogger<AudioPipelineHostedService> _logger;
    private readonly bool _fileMode;

    private int _segmentCount;

    public AudioPipelineHostedService(
        IAudioCaptureSource source,
        VadSegmenter segmenter,
        AppState state,
        ILogger<AudioPipelineHostedService> logger)
    {
        _source = source;
        _segmenter = segmenter;
        _state = state;
        _logger = logger;
        _fileMode = source is WavFileAudioCaptureSource;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _segmenter.SpeechSegmentReady += OnSpeechSegmentReady;
        _source.FrameReady += OnFrameReady;
        _source.CaptureStopped += OnCaptureStopped;
        _state.PausedChanged += OnPausedChanged;

        try
        {
            _source.Start();
            _logger.LogInformation("Pipeline audio uruchomiony ({Mode}).", _fileMode ? "plik" : "mikrofon");
        }
        catch (Exception ex)
        {
            // Brak mikrofonu nie może wywalić aplikacji — tray ma dalej działać.
            _logger.LogError(ex, "Nie udało się uruchomić przechwytywania audio — pipeline nieaktywny.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _source.FrameReady -= OnFrameReady;
        _source.CaptureStopped -= OnCaptureStopped;
        _segmenter.SpeechSegmentReady -= OnSpeechSegmentReady;
        _state.PausedChanged -= OnPausedChanged;

        _source.Stop();
        return Task.CompletedTask;
    }

    private void OnFrameReady(object? sender, AudioFrame frame)
    {
        if (_state.IsPaused)
        {
            return;                                  // pauza: nie karmimy VAD
        }

        _segmenter.Push(frame);
    }

    private void OnSpeechSegmentReady(object? sender, SpeechSegment segment)
    {
        var count = Interlocked.Increment(ref _segmentCount);
        _logger.LogInformation(
            "Segment mowy #{Number}: {Ms} ms ({Samples} próbek).",
            count,
            (int)segment.Duration.TotalMilliseconds,
            segment.Pcm.Length);
    }

    private void OnPausedChanged(object? sender, bool paused)
    {
        if (paused)
        {
            _segmenter.Flush();                      // domknij otwarty segment przy wejściu w pauzę
        }
    }

    private void OnCaptureStopped(object? sender, EventArgs e)
    {
        _segmenter.Flush();                          // domknij ostatni segment
        _logger.LogInformation("Przechwytywanie zakończone — łącznie segmentów: {Count}.", _segmentCount);

        if (_fileMode)
        {
            // Tryb pliku: po przetworzeniu całości zamknij aplikację (weryfikacja/demo).
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                () => System.Windows.Application.Current.Shutdown());
        }
    }
}
