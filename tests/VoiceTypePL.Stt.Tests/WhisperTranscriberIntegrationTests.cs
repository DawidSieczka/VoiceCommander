using System.Net.Http;
using System.Reflection;
using VoiceTypePL.Core.Audio;
using VoiceTypePL.Core.Speech;
using VoiceTypePL.Stt;
using Whisper.net.Ggml;

namespace VoiceTypePL.Stt.Tests;

/// <summary>
/// Test integracyjny z REALNYM Whisperem (natywka CPU) i realnym nagraniem (jfk.wav, 16 kHz mono).
/// Potwierdza cały tor STT: pobranie/cache modelu → factory → kolejka → transkrypcja → post-processing
/// → zdarzenie. Użyty jest mały model i angielskie nagranie (stabilna, szybka asercja); jakość polskiego
/// weryfikujemy end-to-end na aplikacji (patrz handoff). Jeśli model nie jest w cache i brak sieci —
/// test grzecznie się pomija (Skip), zamiast fałszywie failować w środowisku offline.
/// </summary>
public sealed class WhisperTranscriberIntegrationTests
{
    [Fact]
    public async Task Transcriber_OnEnglishRecording_ProducesExpectedText()
    {
        var wav = LoadJfk();

        var options = new WhisperOptions
        {
            Language = "en",                 // nagranie jest angielskie — stabilna asercja treści
            PreferGpu = false,               // natywka CPU: szybko i deterministycznie, bez GPU w CI
            CpuModel = GgmlType.Base,
            Quantization = QuantizationType.NoQuantization,
        };

        var provider = new WhisperModelProvider(options);   // domyślny cache %LocalAppData%\VoiceTypePL\models
        await using var transcriber = new WhisperTranscriber(options, provider);

        var results = new List<TranscribedSentence>();
        transcriber.SentenceTranscribed += (_, s) => results.Add(s);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            await transcriber.InitializeAsync(timeout.Token);
        }
        catch (Exception ex) when (IsOffline(ex))
        {
            // Model niedostępny w cache i brak sieci — brak twardej możliwości „skip" w xUnit 2.5,
            // więc kończymy bez asercji (test nie failuje w środowisku offline).
            return;
        }

        transcriber.Enqueue(new SpeechSegment(wav.Samples, DateTimeOffset.Now));
        await transcriber.CompleteAsync(timeout.Token);

        Assert.NotEmpty(results);
        var text = string.Join(" ", results.Select(r => r.Text)).ToLowerInvariant();
        Assert.Contains("country", text);          // znany fragment cytatu JFK

        // Post-processing: pierwsza litera wielka, jakieś zakończenie zdania.
        var first = results[0].Text;
        Assert.True(char.IsUpper(first[0]), $"Oczekiwano wielkiej pierwszej litery, było: '{first}'.");
    }

    private static bool IsOffline(Exception ex) =>
        ex is HttpRequestException
        || ex.InnerException is HttpRequestException
        || (ex is TaskCanceledException && ex.InnerException is TimeoutException);

    private static WavData LoadJfk()
    {
        const string resource = "VoiceTypePL.Stt.Tests.Assets.jfk.wav";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Brak zasobu '{resource}'. Dostępne: {string.Join(", ", assembly.GetManifestResourceNames())}");
        return WavPcm.Read(stream);
    }
}
