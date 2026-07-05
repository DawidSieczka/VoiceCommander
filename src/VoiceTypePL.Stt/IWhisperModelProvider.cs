using Whisper.net.Ggml;

namespace VoiceTypePL.Stt;

/// <summary>
/// Udostępnia lokalną ścieżkę do modelu ggml Whispera, pobierając go przy pierwszym użyciu (§4).
/// Wydzielone z transcribera, żeby dało się je testować i podmienić (np. model dostarczony z instalatorem).
/// </summary>
public interface IWhisperModelProvider
{
    /// <summary>
    /// Zwraca ścieżkę do modelu danego typu i kwantyzacji. Jeśli pliku nie ma w katalogu modeli,
    /// pobiera go (z raportowaniem postępu). Kolejne wywołania trafiają w plik z cache.
    /// </summary>
    Task<string> GetModelPathAsync(
        GgmlType type,
        QuantizationType quantization,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Postęp pobierania modelu (rozmiar total bywa nieznany, gdy serwer nie poda Content-Length).</summary>
public readonly record struct ModelDownloadProgress(long BytesDownloaded, long? TotalBytes)
{
    /// <summary>Ułamek 0–1, jeśli znany całkowity rozmiar; inaczej <c>null</c>.</summary>
    public double? Fraction => TotalBytes is > 0 ? (double)BytesDownloaded / TotalBytes.Value : null;
}
