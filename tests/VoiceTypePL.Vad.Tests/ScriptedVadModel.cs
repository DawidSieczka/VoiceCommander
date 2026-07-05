using VoiceTypePL.Core.Audio;
using VoiceTypePL.Vad;

namespace VoiceTypePL.Vad.Tests;

/// <summary>
/// Atrapa <see cref="IVadModel"/> zwracająca z góry ustalone prawdopodobieństwa (po jednym na okno).
/// Pozwala testować maszynę stanów <see cref="VadSegmenter"/> deterministycznie, bez ONNX i audio.
/// </summary>
internal sealed class ScriptedVadModel : IVadModel
{
    private readonly Queue<float> _probabilities;

    public ScriptedVadModel(IEnumerable<float> probabilities)
    {
        _probabilities = new Queue<float>(probabilities);
    }

    public int WindowSize => AudioFormat.FrameSamples;

    public int ResetCount { get; private set; }

    public float Process(ReadOnlySpan<float> window)
        => _probabilities.Count > 0 ? _probabilities.Dequeue() : 0f;

    public void Reset() => ResetCount++;

    public void Dispose() { }
}
