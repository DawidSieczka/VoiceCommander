namespace VoiceTypePL.Core.Models;

/// <summary>
/// Minimalny schemat konfiguracji aplikacji (Etap 0 — „Szkielet").
/// Celowo trzy pola; w kolejnych etapach (§5.8) rozszerzany o wybór mikrofonu,
/// model Whispera, próg VAD, strategię wstrzykiwania, hotkeye, autostart i czarną listę.
/// <see cref="SchemaVersion"/> pozwoli w przyszłości migrować starsze pliki configu.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Wersja schematu pliku konfiguracji — punkt zaczepienia dla przyszłych migracji.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Język transkrypcji w formacie ISO 639-1. Domyślnie polski.</summary>
    public string Language { get; set; } = "pl";

    /// <summary>
    /// Minimalny poziom logowania (Serilog): Verbose, Debug, Information, Warning, Error, Fatal.
    /// Sterowany z pliku configu, żeby dało się podbić szczegółowość logów bez rekompilacji.
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>Czy transkrypcja ma preferować GPU (CUDA→Vulkan) zamiast CPU (§5.3).</summary>
    public bool WhisperPreferGpu { get; set; } = true;

    /// <summary>Model Whispera na GPU (nazwa <c>GgmlType</c>, np. <c>LargeV3Turbo</c>).</summary>
    public string WhisperGpuModel { get; set; } = "LargeV3Turbo";

    /// <summary>Model Whispera na CPU (nazwa <c>GgmlType</c>, np. <c>Medium</c>).</summary>
    public string WhisperCpuModel { get; set; } = "Medium";

    /// <summary>Kwantyzacja pobieranego modelu (nazwa <c>QuantizationType</c>, np. <c>Q5_0</c>).</summary>
    public string WhisperQuantization { get; set; } = "Q5_0";
}
