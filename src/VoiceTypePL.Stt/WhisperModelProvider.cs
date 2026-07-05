using Microsoft.Extensions.Logging;
using Whisper.net.Ggml;

namespace VoiceTypePL.Stt;

/// <summary>
/// Pobiera i cache'uje modele ggml Whispera w katalogu na dysku (domyślnie
/// <c>%LocalAppData%\VoiceTypePL\models</c>, §4). Pobieranie idzie do pliku tymczasowego i dopiero
/// po sukcesie jest przenoszone na docelową nazwę — przerwane pobranie nie zostawia uszkodzonego modelu.
/// </summary>
public sealed class WhisperModelProvider : IWhisperModelProvider
{
    private readonly WhisperGgmlDownloader _downloader;
    private readonly ILogger<WhisperModelProvider>? _logger;
    private readonly string _modelsDirectory;

    public WhisperModelProvider(WhisperOptions options, ILogger<WhisperModelProvider>? logger = null)
        : this(options.ModelsDirectory, WhisperGgmlDownloader.Default, logger)
    {
    }

    // Konstruktor dla testów: własny downloader / katalog.
    public WhisperModelProvider(
        string? modelsDirectory,
        WhisperGgmlDownloader downloader,
        ILogger<WhisperModelProvider>? logger = null)
    {
        _downloader = downloader;
        _logger = logger;
        _modelsDirectory = string.IsNullOrWhiteSpace(modelsDirectory)
            ? DefaultModelsDirectory()
            : modelsDirectory;
    }

    public async Task<string> GetModelPathAsync(
        GgmlType type,
        QuantizationType quantization,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_modelsDirectory);
        var fileName = $"ggml-{type.ToString().ToLowerInvariant()}-{quantization.ToString().ToLowerInvariant()}.bin";
        var targetPath = Path.Combine(_modelsDirectory, fileName);

        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            _logger?.LogInformation("Model Whisper z cache: {Path}", targetPath);
            return targetPath;
        }

        _logger?.LogInformation(
            "Pobieram model Whisper {Type} ({Quant}) → {Path} (pierwsze uruchomienie może chwilę potrwać)…",
            type, quantization, targetPath);

        var tempPath = targetPath + ".download";
        try
        {
            using (var modelStream = await _downloader
                .GetGgmlModelAsync(type, quantization, cancellationToken).ConfigureAwait(false))
            using (var fileStream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await CopyWithProgressAsync(modelStream, fileStream, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
            _logger?.LogInformation(
                "Model pobrany: {Path} ({Mb:F0} MB).", targetPath, new FileInfo(targetPath).Length / 1024d / 1024d);
            return targetPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = TryGetLength(source);
        var buffer = new byte[1 << 16];
        long copied = 0;
        long lastReport = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;

            // Raportuj/loguj co ~16 MB, żeby nie zalać logu.
            if (copied - lastReport >= 16 * 1024 * 1024)
            {
                lastReport = copied;
                progress?.Report(new ModelDownloadProgress(copied, totalBytes));
                _logger?.LogInformation("  …pobrano {Mb:F0} MB{Total}",
                    copied / 1024d / 1024d,
                    totalBytes is > 0 ? $" / {totalBytes.Value / 1024d / 1024d:F0} MB" : string.Empty);
            }
        }

        progress?.Report(new ModelDownloadProgress(copied, totalBytes ?? copied));
    }

    private static long? TryGetLength(Stream stream)
    {
        try
        {
            return stream.CanSeek && stream.Length > 0 ? stream.Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // sprzątanie best-effort
        }
    }

    private static string DefaultModelsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceTypePL", "models");
}
