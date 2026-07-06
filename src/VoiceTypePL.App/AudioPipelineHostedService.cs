using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VoiceTypePL.Audio;
using VoiceTypePL.Core.Audio;
using VoiceTypePL.Core.Speech;
using VoiceTypePL.Vad;

namespace VoiceTypePL.App;

/// <summary>
/// Spina pipeline: źródło audio → <see cref="VadSegmenter"/> → <see cref="ITranscriber"/> (Whisper) → log.
/// Respektuje pauzę (nie karmi VAD; wejście w pauzę domyka otwarty segment). W trybie pliku (weryfikacja
/// headless) czeka na gotowość modelu przed podaniem audio, a po przetworzeniu i opróżnieniu kolejki
/// transkrypcji zamyka aplikację.
/// </summary>
public sealed class AudioPipelineHostedService : IHostedService
{
    private readonly IAudioCaptureSource _source;
    private readonly VadSegmenter _segmenter;
    private readonly ITranscriber _transcriber;
    private readonly AppState _state;
    private readonly ILogger<AudioPipelineHostedService> _logger;
    private readonly bool _fileMode;

    private int _segmentCount;
    private int _sentenceCount;

    public AudioPipelineHostedService(
        IAudioCaptureSource source,
        VadSegmenter segmenter,
        ITranscriber transcriber,
        AppState state,
        ILogger<AudioPipelineHostedService> logger)
    {
        _source = source;
        _segmenter = segmenter;
        _transcriber = transcriber;
        _state = state;
        _logger = logger;
        _fileMode = source is WavFileAudioCaptureSource;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _segmenter.SpeechSegmentReady += OnSpeechSegmentReady;
        _transcriber.SentenceTranscribed += OnSentenceTranscribed;
        _source.FrameReady += OnFrameReady;
        _source.CaptureStopped += OnCaptureStopped;
        _state.PausedChanged += OnPausedChanged;

        if (_fileMode)
        {
            // Weryfikacja headless: model musi być gotowy zanim podamy audio (poprawny pomiar latencji,
            // pewność, że żaden segment nie przepadnie przed inicjalizacją).
            await _transcriber.InitializeAsync(cancellationToken);
            StartSource();
        }
        else
        {
            // Mikrofon: nie blokuj startu aplikacji pobieraniem modelu — segmenty buforują się w kolejce,
            // a transkrypcja rusza, gdy model będzie gotowy.
            StartSource();
            _ = InitializeTranscriberInBackground();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _source.FrameReady -= OnFrameReady;
        _source.CaptureStopped -= OnCaptureStopped;
        _segmenter.SpeechSegmentReady -= OnSpeechSegmentReady;
        _transcriber.SentenceTranscribed -= OnSentenceTranscribed;
        _state.PausedChanged -= OnPausedChanged;

        _source.Stop();
        return Task.CompletedTask;
    }

    private void StartSource()
    {
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
    }

    private async Task InitializeTranscriberInBackground()
    {
        try
        {
            await _transcriber.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się zainicjalizować transkrypcji (model Whisper).");
        }
    }

    private void OnFrameReady(object? sender, AudioFrame frame)
    {
        _state.SignalLevel = frame.Rms;              // wskaźnik poziomu w UI działa też podczas pauzy

        if (_state.IsEffectivelyPaused)
        {
            return;                                  // pauza (ręczna lub czarna lista): nie karmimy VAD
        }

        _segmenter.Push(frame);
    }

    private void OnSpeechSegmentReady(object? sender, SpeechSegment segment)
    {
        var count = Interlocked.Increment(ref _segmentCount);
        _logger.LogInformation(
            "Segment mowy #{Number}: {Ms} ms ({Samples} próbek) → kolejka STT.",
            count,
            (int)segment.Duration.TotalMilliseconds,
            segment.Pcm.Length);

        _transcriber.Enqueue(segment);
    }

    private void OnSentenceTranscribed(object? sender, TranscribedSentence sentence)
    {
        var count = Interlocked.Increment(ref _sentenceCount);
        _logger.LogInformation(
            "Transkrypcja #{Number} ({AudioMs} ms audio, {ProcMs} ms przetwarzania): \"{Text}\"",
            count,
            (int)sentence.AudioDuration.TotalMilliseconds,
            (int)sentence.ProcessingDuration.TotalMilliseconds,
            sentence.Text);
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
            // Tryb pliku: dokończ transkrypcję zakolejkowanych segmentów, dopiero potem zamknij aplikację.
            _ = DrainAndShutdownAsync();
        }
    }

    private async Task DrainAndShutdownAsync()
    {
        try
        {
            await _transcriber.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd przy opróżnianiu kolejki transkrypcji.");
        }

        _logger.LogInformation("Transkrypcja zakończona — łącznie zdań: {Count}.", _sentenceCount);

        // W trybie Direct ostatnie zdanie może być właśnie wpisywane (klik + Ctrl+V + przywrócenie
        // schowka rozłożone na await'y na Dispatcherze) — daj mu chwilę przed zamknięciem aplikacji.
        await Task.Delay(TimeSpan.FromSeconds(2));

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => System.Windows.Application.Current.Shutdown());
    }
}
