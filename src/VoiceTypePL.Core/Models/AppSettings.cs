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

    /// <summary>Po ilu sekundach dymek sam znika (0 = nigdy). §5.4.</summary>
    public double OverlayAutoHideSeconds { get; set; } = 12;

    /// <summary>Strategia wstrzykiwania: <c>Clipboard</c> (domyślnie) lub <c>UnicodeSendInput</c> (§5.5).</summary>
    public string InjectionStrategy { get; set; } = "Clipboard";

    /// <summary>
    /// Czy klikać w pozycji kursora dla ustawienia fokusu (§5.5). Domyślnie WYŁĄCZONE — tekst trafia
    /// w miejsce bieżącej karetki w oknie z fokusem (kolejne zdania po sobie), a nie w pozycję myszy.
    /// </summary>
    public bool InjectionClickToFocus { get; set; }

    /// <summary>Opóźnienie [ms] przywrócenia schowka po wklejeniu.</summary>
    public int InjectionClipboardRestoreDelayMs { get; set; } = 150;

    /// <summary>Czy dopiąć spację separującą po wpisanym zdaniu (§5.5.4).</summary>
    public bool InjectionAppendSpace { get; set; } = true;

    /// <summary>Czy pomijać pola hasła przy wstrzykiwaniu (§5.5).</summary>
    public bool InjectionSkipPasswordFields { get; set; } = true;
}
