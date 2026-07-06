using Whisper.net.Ggml;

namespace VoiceTypePL.Stt;

/// <summary>
/// Konfiguracja transkrypcji Whisperem (§4, §5.3). Domyślnie polski, temperatura 0 (deterministyczne
/// dekodowanie). Model dobierany wg dostępności GPU: <c>large-v3-turbo</c> (q5) na GPU, <c>medium</c>
/// (q5) na CPU — najlepszy kompromis jakości polskiego i szybkości.
/// </summary>
public sealed class WhisperOptions
{
    /// <summary>Język transkrypcji (ISO 639-1). Domyślnie polski.</summary>
    public string Language { get; set; } = "pl";

    /// <summary>Temperatura dekodowania. 0 = deterministycznie (§5.3).</summary>
    public float Temperature { get; set; } = 0f;

    /// <summary>Czy próbować GPU (CUDA→Vulkan). Przy braku natywki loader schodzi na CPU.</summary>
    public bool PreferGpu { get; set; } = true;

    /// <summary>Model używany, gdy dostępne GPU.</summary>
    public GgmlType GpuModel { get; set; } = GgmlType.LargeV3Turbo;

    /// <summary>Model używany na CPU (szybszy, mniejszy).</summary>
    public GgmlType CpuModel { get; set; } = GgmlType.Medium;

    /// <summary>Kwantyzacja pobieranego modelu ggml.</summary>
    public QuantizationType Quantization { get; set; } = QuantizationType.Q5_0;

    /// <summary>
    /// Liczba wątków dekodowania (istotna na CPU). 0 = automatycznie: liczba rdzeni logicznych
    /// ograniczona do 8 (powyżej zysk znika, a maszyna robi się nieresponsywna).
    /// </summary>
    public int Threads { get; set; }

    /// <summary>
    /// Katalog na modele. <c>null</c> → <c>%LocalAppData%\VoiceTypePL\models</c> (§4).
    /// </summary>
    public string? ModelsDirectory { get; set; }

    /// <summary>Parametry post-processingu i filtra halucynacji.</summary>
    public TranscriptPostProcessorOptions PostProcessing { get; set; } = new();
}
