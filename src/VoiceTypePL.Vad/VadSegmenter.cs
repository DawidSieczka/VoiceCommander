using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.Audio;
using VoiceTypePL.Core.Speech;

namespace VoiceTypePL.Vad;

/// <summary>
/// Skleja ramki audio w segmenty mowy (zdania) na podstawie prawdopodobieństw z <see cref="IVadModel"/>.
/// Logika wzorowana na Silero <c>get_speech_timestamps</c> (§5.2): histereza progu, padding ciszy
/// przed/po, odrzucanie zbyt krótkich segmentów, przymusowe domknięcie po maksymalnej długości.
/// Wejście przyjmuje dowolne bufory próbek i wewnętrznie tnie je na okna <see cref="IVadModel.WindowSize"/>.
/// </summary>
public sealed class VadSegmenter : ISpeechSegmentSource
{
    private readonly IVadModel _model;
    private readonly VadOptions _options;
    private readonly ILogger<VadSegmenter>? _logger;

    private readonly int _windowSize;
    private readonly int _prePadSamples;
    private readonly int _postPadSamples;
    private readonly int _minSilenceSamples;
    private readonly int _minSpeechSamples;
    private readonly int _maxSpeechSamples;

    // Akumulator do składania pełnych okien z dowolnych buforów wejściowych.
    private readonly float[] _window;
    private int _windowFill;

    // Bufor pre-paddingu: ostatnie próbki sprzed wyzwolenia mowy.
    private readonly List<float> _preBuffer = new();

    // Bieżący segment (od wyzwolenia): pre-pad + mowa + narastająca cisza końcowa.
    private readonly List<float> _current = new();
    private bool _triggered;
    private int _prePadInCurrent;      // ile próbek na początku _current to pre-pad
    private int _trailingSilence;      // długość nieprzerwanej ciszy na końcu _current
    private int _speechSamples;        // przybliżona długość samej mowy w segmencie

    public VadSegmenter(IVadModel model, VadOptions options, ILogger<VadSegmenter>? logger = null)
    {
        _model = model;
        _options = options;
        _logger = logger;

        _windowSize = model.WindowSize;
        _window = new float[_windowSize];
        _prePadSamples = AudioFormat.DurationToSamples(options.SpeechPadding);
        _postPadSamples = AudioFormat.DurationToSamples(options.SpeechPadding);
        _minSilenceSamples = AudioFormat.DurationToSamples(options.MinSilenceDuration);
        _minSpeechSamples = AudioFormat.DurationToSamples(options.MinSpeechDuration);
        _maxSpeechSamples = AudioFormat.DurationToSamples(options.MaxSpeechDuration);
    }

    public event EventHandler<SpeechSegment>? SpeechSegmentReady;

    /// <summary>Podaje ramkę audio (16 kHz mono) do analizy.</summary>
    public void Push(AudioFrame frame) => Push(frame.Samples);

    /// <summary>Podaje dowolny bufor próbek (16 kHz mono, [-1, 1]).</summary>
    public void Push(ReadOnlySpan<float> samples)
    {
        var offset = 0;
        while (offset < samples.Length)
        {
            var take = Math.Min(_windowSize - _windowFill, samples.Length - offset);
            samples.Slice(offset, take).CopyTo(_window.AsSpan(_windowFill));
            _windowFill += take;
            offset += take;

            if (_windowFill == _windowSize)
            {
                ProcessWindow(_window);
                _windowFill = 0;
            }
        }
    }

    /// <summary>Kończy strumień: domyka ewentualny otwarty segment i zeruje stan modelu/buforów.</summary>
    public void Flush()
    {
        if (_triggered)
        {
            FinalizeSegment(trimTrailingSilence: true);
        }

        _windowFill = 0;
        _preBuffer.Clear();
        _current.Clear();
        _triggered = false;
        _prePadInCurrent = 0;
        _trailingSilence = 0;
        _speechSamples = 0;
        _model.Reset();
    }

    private void ProcessWindow(ReadOnlySpan<float> window)
    {
        var probability = _model.Process(window);
        var isSpeech = probability >= _options.SpeechThreshold;
        var isSilence = probability < _options.SilenceThreshold;

        if (!_triggered)
        {
            if (isSpeech)
            {
                // Wyzwolenie: zainicjuj segment pre-paddingiem, potem bieżącym oknem.
                _current.Clear();
                _current.AddRange(_preBuffer);
                _prePadInCurrent = _preBuffer.Count;
                _current.AddRange(window);
                _speechSamples = window.Length;
                _trailingSilence = 0;
                _triggered = true;
            }
        }
        else
        {
            _current.AddRange(window);

            if (isSilence)
            {
                _trailingSilence += window.Length;
            }
            else
            {
                _trailingSilence = 0;
                if (isSpeech)
                {
                    _speechSamples += window.Length;
                }
            }

            if (_trailingSilence >= _minSilenceSamples)
            {
                FinalizeSegment(trimTrailingSilence: true);
            }
            else if (_current.Count - _prePadInCurrent >= _maxSpeechSamples)
            {
                _logger?.LogDebug("Segment osiągnął maksymalną długość — domykam na siłę.");
                FinalizeSegment(trimTrailingSilence: false);
            }
        }

        // Aktualizacja bufora pre-paddingu (okna do bieżącego włącznie, dla NASTĘPNEGO wyzwolenia).
        _preBuffer.AddRange(window);
        if (_preBuffer.Count > _prePadSamples)
        {
            _preBuffer.RemoveRange(0, _preBuffer.Count - _prePadSamples);
        }
    }

    private void FinalizeSegment(bool trimTrailingSilence)
    {
        var length = _current.Count;
        if (trimTrailingSilence && _trailingSilence > _postPadSamples)
        {
            length -= _trailingSilence - _postPadSamples;   // zostaw tylko post-padding ciszy
        }

        if (_speechSamples >= _minSpeechSamples && length > 0)
        {
            var pcm = new float[length];
            _current.CopyTo(0, pcm, 0, length);
            var segment = new SpeechSegment(pcm, DateTimeOffset.Now);
            _logger?.LogDebug(
                "Domknięto segment: {Duration} ms ({Samples} próbek, mowa ~{Speech} ms).",
                (int)segment.Duration.TotalMilliseconds, length,
                (int)AudioFormat.SamplesToDuration(_speechSamples).TotalMilliseconds);
            SpeechSegmentReady?.Invoke(this, segment);
        }
        else
        {
            _logger?.LogDebug(
                "Odrzucono segment: mowa ~{Speech} ms < min {Min} ms.",
                (int)AudioFormat.SamplesToDuration(_speechSamples).TotalMilliseconds,
                (int)_options.MinSpeechDuration.TotalMilliseconds);
        }

        _current.Clear();
        _triggered = false;
        _prePadInCurrent = 0;
        _trailingSilence = 0;
        _speechSamples = 0;
    }
}
