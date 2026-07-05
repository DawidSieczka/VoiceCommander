using System.Reflection;
using VoiceTypePL.Core.Audio;
using VoiceTypePL.Core.Speech;
using VoiceTypePL.Vad;

namespace VoiceTypePL.Vad.Tests;

/// <summary>
/// Testy z REALNYM modelem Silero (ONNX) i realnym nagraniem mowy (jfk.wav, 16 kHz mono).
/// Potwierdzają, że kontrakt I/O modelu jest poprawny i że pipeline VAD wykrywa segmenty mowy.
/// VAD jest językowo-niezależny, więc angielskie nagranie w zupełności wystarcza do weryfikacji.
/// </summary>
public sealed class SileroVadIntegrationTests
{
    [Fact]
    public void EmbeddedWav_Is16kHzMono()
    {
        var wav = LoadJfk();
        Assert.Equal(16_000, wav.SampleRate);
        Assert.Equal(1, wav.Channels);
        Assert.True(wav.Samples.Length > 16_000, "Nagranie powinno mieć co najmniej 1 s.");
    }

    [Fact]
    public void Model_OnSilence_ReturnsLowProbability()
    {
        using var model = new SileroVadModel();
        var silence = new float[model.WindowSize];

        var probability = 1f;
        for (var i = 0; i < 20; i++)          // ustabilizuj stan na ciszy
        {
            probability = model.Process(silence);
        }

        Assert.True(probability < 0.5f, $"Cisza powinna dać niskie prawdopodobieństwo, było {probability}.");
    }

    [Fact]
    public void Pipeline_OnSpeechRecording_DetectsSpeechSegments()
    {
        var wav = LoadJfk();

        using var model = new SileroVadModel();
        var segmenter = new VadSegmenter(model, new VadOptions());
        var segments = new List<SpeechSegment>();
        segmenter.SpeechSegmentReady += (_, s) => segments.Add(s);

        segmenter.Push(wav.Samples);
        segmenter.Flush();

        Assert.NotEmpty(segments);

        var totalSpeech = segments.Sum(s => s.Duration.TotalSeconds);
        Assert.True(totalSpeech > 3.0, $"Oczekiwano >3 s mowy, wykryto {totalSpeech:F1} s.");

        foreach (var segment in segments)
        {
            Assert.InRange(segment.Duration.TotalMilliseconds, 300, 30_000);
        }
    }

    private static WavData LoadJfk()
    {
        const string resource = "VoiceTypePL.Vad.Tests.Assets.jfk.wav";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Brak zasobu '{resource}'. Dostępne: {string.Join(", ", assembly.GetManifestResourceNames())}");
        return WavPcm.Read(stream);
    }
}
