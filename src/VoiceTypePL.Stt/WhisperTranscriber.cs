using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.Speech;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace VoiceTypePL.Stt;

/// <summary>
/// Transkrypcja Whisper.net (§5.3). Jedna instancja <see cref="WhisperFactory"/> (model w pamięci),
/// segmenty mowy przechodzą przez kolejkę (<see cref="Channel{T}"/>) i są przetwarzane sekwencyjnie na
/// osobnym wątku. Runtime dobierany automatycznie: CUDA → Vulkan → CPU (loader Whisper.net wybiera
/// pierwszą dostępną natywkę). Po transkrypcji tekst przechodzi przez <see cref="TranscriptPostProcessor"/>
/// (porządkowanie + filtr halucynacji) i — jeśli przejdzie — jest emitowany jako <see cref="SentenceTranscribed"/>.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private readonly WhisperOptions _options;
    private readonly IWhisperModelProvider _modelProvider;
    private readonly TranscriptPostProcessor _postProcessor;
    private readonly ILogger<WhisperTranscriber>? _logger;

    private readonly Channel<SpeechSegment> _queue =
        Channel.CreateUnbounded<SpeechSegment>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private Task? _consumer;
    private volatile bool _initialized;

    public WhisperTranscriber(
        WhisperOptions options,
        IWhisperModelProvider modelProvider,
        ILogger<WhisperTranscriber>? logger = null)
    {
        _options = options;
        _modelProvider = modelProvider;
        _postProcessor = new TranscriptPostProcessor(options.PostProcessing);
        _logger = logger;
    }

    public event EventHandler<TranscribedSentence>? SentenceTranscribed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            // Kolejność prób natywek: GPU (CUDA→Vulkan) przed CPU, chyba że wymuszono CPU.
            RuntimeOptions.RuntimeLibraryOrder = _options.PreferGpu
                ? new List<RuntimeLibrary> { RuntimeLibrary.Cuda, RuntimeLibrary.Cuda12, RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu }
                : new List<RuntimeLibrary> { RuntimeLibrary.Cpu };

            var modelType = _options.PreferGpu ? _options.GpuModel : _options.CpuModel;
            var modelPath = await _modelProvider
                .GetModelPathAsync(modelType, _options.Quantization, progress: null, cancellationToken)
                .ConfigureAwait(false);

            _factory = WhisperFactory.FromPath(modelPath);
            _processor = _factory.CreateBuilder()
                .WithLanguage(_options.Language)
                .WithTemperature(_options.Temperature)
                .WithNoContext()          // każdy segment VAD niezależnie — bez przenoszenia kontekstu
                .WithProbabilities()      // wypełnia Probability / NoSpeechProbability (filtr halucynacji)
                .Build();

            var loaded = RuntimeOptions.LoadedLibrary;
            _logger?.LogInformation(
                "Whisper gotowy — model {Model} ({Quant}), runtime {Loaded}.",
                modelType, _options.Quantization, loaded?.ToString() ?? "auto");
            if (_options.PreferGpu && loaded == RuntimeLibrary.Cpu)
            {
                _logger?.LogWarning(
                    "GPU niedostępne — model {Model} działa na CPU (wolno). Rozważ model {Cpu} dla CPU.",
                    modelType, _options.CpuModel);
            }

            _consumer = Task.Run(ConsumeAsync);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Enqueue(SpeechSegment segment)
    {
        if (!_queue.Writer.TryWrite(segment))
        {
            _logger?.LogWarning("Nie udało się zakolejkować segmentu — kolejka transkrypcji zamknięta.");
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        _queue.Writer.TryComplete();
        if (_consumer is not null)
        {
            await _consumer.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ConsumeAsync()
    {
        var reader = _queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var segment))
                {
                    await TranscribeAsync(segment).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // zamknięcie aplikacji — normalne
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Wątek transkrypcji zakończony nieoczekiwanym błędem.");
        }
    }

    private async Task TranscribeAsync(SpeechSegment segment)
    {
        if (_processor is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var builder = new StringBuilder();
        var minNoSpeech = 1f;      // najniższe no_speech = najbardziej „mowa" spośród pod-segmentów
        var sumProb = 0f;
        var count = 0;

        try
        {
            await foreach (var result in _processor.ProcessAsync(segment.Pcm, _cts.Token).ConfigureAwait(false))
            {
                builder.Append(result.Text);
                minNoSpeech = Math.Min(minNoSpeech, result.NoSpeechProbability);
                sumProb += result.Probability;
                count++;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Błąd transkrypcji segmentu ({Ms} ms).", (int)segment.Duration.TotalMilliseconds);
            return;
        }

        stopwatch.Stop();

        var avgProbability = count > 0 ? sumProb / count : 1f;
        var noSpeechProbability = count > 0 ? minNoSpeech : 1f;
        var raw = builder.ToString();

        var clean = _postProcessor.Process(raw, noSpeechProbability, avgProbability);
        if (clean is null)
        {
            _logger?.LogDebug(
                "Segment odrzucony (cisza/halucynacja): no_speech={NoSpeech:F2}, prob={Prob:F2}, raw='{Raw}'.",
                noSpeechProbability, avgProbability, raw.Trim());
            return;
        }

        var sentence = new TranscribedSentence(clean, segment.Duration, stopwatch.Elapsed, DateTimeOffset.Now);
        SentenceTranscribed?.Invoke(this, sentence);
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _cts.Cancel();

        if (_consumer is not null)
        {
            try
            {
                await _consumer.ConfigureAwait(false);
            }
            catch
            {
                // zamknięcie — błędy nieistotne
            }
        }

        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
        }

        _factory?.Dispose();
        _cts.Dispose();
        _initLock.Dispose();
    }
}
