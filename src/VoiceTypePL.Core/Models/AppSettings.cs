namespace VoiceTypePL.Core.Models;

/// <summary>
/// Schemat konfiguracji aplikacji (§5.8). Od Etapu 7 obejmuje tryb dyktowania, próg ciszy VAD,
/// autostart i czarną listę aplikacji; edytowany z okna ustawień (zapis do %AppData%).
/// <see cref="SchemaVersion"/> pozwoli w przyszłości migrować starsze pliki configu.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Wersja schematu pliku konfiguracji — punkt zaczepienia dla przyszłych migracji.</summary>
    public int SchemaVersion { get; set; } = 3;

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

    /// <summary>
    /// Tryb dyktowania: <c>Direct</c> (domyślny — zdania wpisywane od razu w okno z fokusem, bez dymka;
    /// dymek służy tylko trybowi edycji Ctrl+Alt+E) lub <c>Confirm</c> (każde zdanie czeka w dymku na
    /// Enter/Esc, jak w §5.4).
    /// </summary>
    public string DictationMode { get; set; } = "Direct";

    /// <summary>
    /// Cisza kończąca zdanie [ms] (§5.2: zakres 400–1200). Niższa wartość = szybsza reakcja
    /// (tekst pojawia się wcześniej), ale większe ryzyko ucięcia zdania przy wolnym mówieniu.
    /// </summary>
    public int VadMinSilenceMs { get; set; } = 500;

    /// <summary>
    /// Liczba wątków dekodowania Whispera na CPU. 0 = automatycznie (liczba rdzeni, max 8).
    /// </summary>
    public int WhisperThreads { get; set; }

    /// <summary>Czy uruchamiać aplikację przy logowaniu do Windows (klucz HKCU\...\Run, §5.8).</summary>
    public bool AutostartEnabled { get; set; }

    /// <summary>
    /// Czarna lista procesów (nazwy bez rozszerzenia, np. <c>witcher3</c>): gdy okno pierwszoplanowe
    /// należy do procesu z listy, nasłuch jest automatycznie pauzowany (§5.8).
    /// </summary>
    public List<string> BlacklistProcesses { get; set; } = new();
}
