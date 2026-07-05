using VoiceTypePL.Core.Audio;
using VoiceTypePL.Core.Speech;
using VoiceTypePL.Vad;

namespace VoiceTypePL.Vad.Tests;

/// <summary>
/// Deterministyczne testy maszyny stanów segmentacji — model VAD jest zaskryptowany
/// (<see cref="ScriptedVadModel"/>), więc nie zależą od ONNX ani od audio.
/// Jedno okno = <see cref="AudioFormat.FrameSamples"/> próbek; przy 16 kHz to ~32 ms.
/// </summary>
public sealed class VadSegmenterTests
{
    private const int Window = AudioFormat.FrameSamples;

    // ~700 ms ciszy to ~22 okna; ~300 ms mowy to ~10 okien. Używamy zapasu.
    private const int SpeechWindows = 15;   // ~480 ms mowy — powyżej progu min
    private const int SilenceWindows = 30;  // ~960 ms ciszy — powyżej progu końca zdania

    [Fact]
    public void SpeechFollowedBySilence_EmitsSingleSegment()
    {
        var segments = Run(Script(
            (1.0f, SpeechWindows),
            (0.0f, SilenceWindows)));

        Assert.Single(segments);
        Assert.InRange(segments[0].Duration.TotalMilliseconds, 300, 30_000);
    }

    [Fact]
    public void SpeechTooShort_IsRejected()
    {
        var segments = Run(Script(
            (1.0f, 5),                       // ~160 ms — poniżej progu min 300 ms
            (0.0f, SilenceWindows)));

        Assert.Empty(segments);
    }

    [Fact]
    public void TwoSpeechBursts_EmitTwoSegments()
    {
        var segments = Run(Script(
            (1.0f, SpeechWindows),
            (0.0f, SilenceWindows),
            (1.0f, SpeechWindows),
            (0.0f, SilenceWindows)));

        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void Flush_ClosesOpenSegmentAndResetsModel()
    {
        var model = new ScriptedVadModel(Script((1.0f, SpeechWindows)));
        var segmenter = new VadSegmenter(model, new VadOptions());
        var segments = new List<SpeechSegment>();
        segmenter.SpeechSegmentReady += (_, s) => segments.Add(s);

        Feed(segmenter, SpeechWindows);      // mowa bez ciszy końcowej
        Assert.Empty(segments);              // segment jeszcze otwarty

        segmenter.Flush();
        Assert.Single(segments);             // Flush domyka segment
        Assert.Equal(1, model.ResetCount);   // i resetuje stan modelu
    }

    [Fact]
    public void SilenceOnly_EmitsNothing()
    {
        var segments = Run(Script((0.0f, 100)));
        Assert.Empty(segments);
    }

    private static List<SpeechSegment> Run(IReadOnlyList<float> script)
    {
        var model = new ScriptedVadModel(script);
        var segmenter = new VadSegmenter(model, new VadOptions());
        var segments = new List<SpeechSegment>();
        segmenter.SpeechSegmentReady += (_, s) => segments.Add(s);

        Feed(segmenter, script.Count);
        segmenter.Flush();
        return segments;
    }

    private static void Feed(VadSegmenter segmenter, int windows)
    {
        // Zawartość próbek jest bez znaczenia — model jest zaskryptowany. Liczy się liczba okien.
        var buffer = new float[windows * Window];
        segmenter.Push(buffer);
    }

    private static List<float> Script(params (float Probability, int Windows)[] runs)
    {
        var script = new List<float>();
        foreach (var (probability, windows) in runs)
        {
            for (var i = 0; i < windows; i++)
            {
                script.Add(probability);
            }
        }

        return script;
    }
}
