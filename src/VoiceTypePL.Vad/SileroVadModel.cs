using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VoiceTypePL.Core.Audio;

namespace VoiceTypePL.Vad;

/// <summary>
/// Silero VAD v5 na ONNX Runtime (CPU). Model jest zaszyty jako zasób osadzony (~2 MB), więc VAD
/// działa bez pobierania z sieci i bez plików obok EXE. Kontrakt I/O v5:
///   wejścia:  input [1, 64+512] float, state [2, 1, 128] float, sr [] int64
///   wyjścia:  output [1, 1] float (prob mowy), stateN [2, 1, 128] float
/// Uwaga v5: do każdego okna 512 próbek trzeba DOKLEIĆ z przodu 64 próbki „kontekstu" (ogon
/// poprzedniego okna, na starcie zera) — bez tego model liczy się bez błędu, ale zwraca ~0.
/// Stan rekurencyjny (LSTM) i kontekst utrzymujemy między oknami; zerujemy je przez <see cref="Reset"/>.
/// </summary>
public sealed class SileroVadModel : IVadModel
{
    private const string ResourceName = "VoiceTypePL.Vad.Assets.silero_vad.onnx";
    private const int StateSize = 2 * 1 * 128;
    private const int ContextSize = 64;                 // 64 próbki @16 kHz

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _stateName;
    private readonly string _srName;
    private readonly string _outputName;
    private readonly string _stateOutName;

    private float[] _state = new float[StateSize];
    private float[] _context = new float[ContextSize];

    public SileroVadModel(ILogger<SileroVadModel>? logger = null)
    {
        var modelBytes = LoadEmbeddedModel();
        _session = new InferenceSession(modelBytes);

        var inputs = _session.InputMetadata.Keys.ToArray();
        var outputs = _session.OutputMetadata.Keys.ToArray();
        logger?.LogDebug("Silero VAD I/O — wejścia: [{Inputs}], wyjścia: [{Outputs}]",
            string.Join(", ", inputs), string.Join(", ", outputs));

        // Nazwy wg kontraktu v5, z awaryjnym dopasowaniem, gdyby model miał inne etykiety.
        _inputName = Pick(inputs, "input") ?? inputs[0];
        _stateName = Pick(inputs, "state") ?? inputs.FirstOrDefault(n => n != _inputName && n != Pick(inputs, "sr"))
            ?? throw new InvalidOperationException("Model Silero nie ma wejścia stanu — nieoczekiwany wariant.");
        _srName = Pick(inputs, "sr") ?? throw new InvalidOperationException("Model Silero nie ma wejścia 'sr'.");
        _outputName = Pick(outputs, "output") ?? outputs[0];
        _stateOutName = Pick(outputs, "stateN") ?? outputs.First(n => n != _outputName);
    }

    public int WindowSize => AudioFormat.FrameSamples;

    public float Process(ReadOnlySpan<float> window)
    {
        if (window.Length != WindowSize)
        {
            throw new ArgumentException(
                $"Okno musi mieć {WindowSize} próbek, otrzymano {window.Length}.", nameof(window));
        }

        // Wejście = kontekst (64) + okno (512); model v5 wymaga tego doklejenia.
        var input = new DenseTensor<float>(new[] { 1, ContextSize + WindowSize });
        _context.CopyTo(input.Buffer.Span);
        window.CopyTo(input.Buffer.Span[ContextSize..]);

        // Kontekst na następne okno = ostatnie 64 próbki bieżącego okna.
        window[^ContextSize..].CopyTo(_context);

        var state = new DenseTensor<float>(_state, new[] { 2, 1, 128 });
        var sr = new DenseTensor<long>(new long[] { AudioFormat.SampleRate }, Array.Empty<int>());

        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputName, input),
            NamedOnnxValue.CreateFromTensor(_stateName, state),
            NamedOnnxValue.CreateFromTensor(_srName, sr),
        };

        using var results = _session.Run(feeds);

        float probability = 0f;
        foreach (var result in results)
        {
            if (result.Name == _outputName)
            {
                probability = result.AsEnumerable<float>().First();
            }
            else if (result.Name == _stateOutName)
            {
                _state = result.AsEnumerable<float>().ToArray();
            }
        }

        return probability;
    }

    public void Reset()
    {
        _state = new float[StateSize];
        _context = new float[ContextSize];
    }

    public void Dispose() => _session.Dispose();

    private static string? Pick(string[] names, string wanted) =>
        names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));

    private static byte[] LoadEmbeddedModel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Nie znaleziono osadzonego modelu '{ResourceName}'. Dostępne zasoby: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
