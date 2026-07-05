using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using VoiceTypePL.App.Editing;
using VoiceTypePL.App.Overlay;
using VoiceTypePL.App.Tray;
using VoiceTypePL.Audio;
using VoiceTypePL.Core.Audio;
using VoiceTypePL.Core.Configuration;
using VoiceTypePL.Core.History;
using VoiceTypePL.Core.Injection;
using VoiceTypePL.Core.Speech;
using VoiceTypePL.EditEngine;
using VoiceTypePL.Injection;
using VoiceTypePL.Stt;
using VoiceTypePL.Vad;
using Whisper.net.Ggml;

namespace VoiceTypePL.App;

/// <summary>
/// Punkt wejścia aplikacji WPF. Buduje Generic Host (DI + logowanie Serilog),
/// startuje go bez blokowania pętli komunikatów WPF i pokazuje ikonę w zasobniku.
/// </summary>
public partial class App : Application
{
    // Poziom logowania sterowany z configu w locie (ustawiany po wczytaniu ustawień).
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

    private IHost? _host;
    private TrayIconService? _tray;
    private OverlayService? _overlay;
    private EditModeService? _editMode;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceTypePL", "logs");
        Directory.CreateDirectory(logDirectory);

        var builder = Host.CreateApplicationBuilder(e.Args);

        builder.Services.AddSerilog(configuration => configuration
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(logDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<AppState>();
        builder.Services.AddSingleton<TrayIconService>();

        // Pipeline audio + VAD (Etap 1).
        builder.Services.AddSingleton<VadOptions>();
        builder.Services.AddSingleton<IVadModel>(sp =>
            new SileroVadModel(sp.GetRequiredService<ILogger<SileroVadModel>>()));
        builder.Services.AddSingleton<VadSegmenter>();
        builder.Services.AddSingleton<IAudioCaptureSource>(CreateAudioSource);

        // STT — Whisper.net (Etap 2).
        builder.Services.AddSingleton(CreateWhisperOptions);
        builder.Services.AddSingleton<IWhisperModelProvider>(sp =>
            new WhisperModelProvider(
                sp.GetRequiredService<WhisperOptions>(),
                sp.GetRequiredService<ILogger<WhisperModelProvider>>()));
        builder.Services.AddSingleton<ITranscriber>(sp =>
            new WhisperTranscriber(
                sp.GetRequiredService<WhisperOptions>(),
                sp.GetRequiredService<IWhisperModelProvider>(),
                sp.GetRequiredService<ILogger<WhisperTranscriber>>()));

        // Wstrzykiwanie tekstu (Etap 4).
        builder.Services.AddSingleton(CreateInjectionOptions);
        builder.Services.AddSingleton<ITextInjector>(sp =>
            new Win32TextInjector(
                sp.GetRequiredService<InjectionOptions>(),
                sp.GetRequiredService<ILogger<Win32TextInjector>>()));
        builder.Services.AddSingleton<SentenceRegistry>();

        // Dymek potwierdzenia (Etap 3).
        builder.Services.AddSingleton(sp => new OverlayOptions
        {
            AutoHideSeconds = sp.GetRequiredService<ISettingsService>().Current.OverlayAutoHideSeconds,
        });
        builder.Services.AddSingleton<OverlayService>();

        // Edycja zdania — UIA Poziom 1 (Etap 5).
        builder.Services.AddSingleton(sp =>
            new UiaSentenceLocator(sp.GetRequiredService<ILogger<UiaSentenceLocator>>()));
        builder.Services.AddSingleton<EditModeService>();

        builder.Services.AddHostedService<AudioPipelineHostedService>();

        _host = builder.Build();
        await _host.StartAsync();

        // Zastosuj poziom logowania z pliku konfiguracji (SettingsService wczytuje go w konstruktorze).
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        if (Enum.TryParse<LogEventLevel>(settings.Current.LogLevel, ignoreCase: true, out var level))
        {
            LevelSwitch.MinimumLevel = level;
        }

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation(
            "VoiceType PL uruchomiony (język: {Language}, config: {ConfigPath})",
            settings.Current.Language,
            settings.ConfigFilePath);

        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Nieobsłużony wyjątek na wątku UI");
            args.Handled = true;
        };

        _tray = _host.Services.GetRequiredService<TrayIconService>();
        _tray.Initialize();

        // Dymek: tworzenie okna + globalny hook Enter/Esc na wątku UI (jak zasobnik).
        _overlay = _host.Services.GetRequiredService<OverlayService>();
        _overlay.Initialize();

        // Tryb edycji: skrót Ctrl+Alt+E + okno podświetlenia (wątek UI).
        _editMode = _host.Services.GetRequiredService<EditModeService>();
        _editMode.Initialize();
    }

    /// <summary>
    /// Wybiera źródło audio: domyślnie mikrofon (WASAPI). Jeśli zmienna środowiskowa
    /// <c>VOICETYPEPL_AUDIO_FILE</c> wskazuje istniejący plik WAV (16 kHz mono) — używa go
    /// zamiast mikrofonu (weryfikacja headless / demo bez sprzętu).
    /// </summary>
    private static IAudioCaptureSource CreateAudioSource(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<App>>();
        var file = Environment.GetEnvironmentVariable("VOICETYPEPL_AUDIO_FILE");
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
        {
            logger.LogInformation("Źródło audio: plik {File}", file);
            return new WavFileAudioCaptureSource(file);
        }

        logger.LogInformation("Źródło audio: mikrofon (WASAPI)");
        return new WasapiAudioCaptureSource(services.GetRequiredService<ILogger<WasapiAudioCaptureSource>>());
    }

    /// <summary>
    /// Buduje <see cref="WhisperOptions"/> z konfiguracji aplikacji (§4/§5.3). Nazwy modeli i kwantyzacji
    /// są w configu tekstem (Core nie zależy od Whispera) — tu mapujemy je na enumy, z fallbackiem do
    /// wartości domyślnych, gdyby config zawierał nieznaną nazwę.
    /// </summary>
    private static WhisperOptions CreateWhisperOptions(IServiceProvider services)
    {
        var settings = services.GetRequiredService<ISettingsService>().Current;
        var logger = services.GetRequiredService<ILogger<App>>();

        var options = new WhisperOptions
        {
            Language = settings.Language,
            PreferGpu = settings.WhisperPreferGpu,
            GpuModel = ParseEnum(settings.WhisperGpuModel, GgmlType.LargeV3Turbo, logger),
            CpuModel = ParseEnum(settings.WhisperCpuModel, GgmlType.Medium, logger),
            Quantization = ParseEnum(settings.WhisperQuantization, QuantizationType.Q5_0, logger),
        };
        return options;
    }

    /// <summary>Buduje <see cref="InjectionOptions"/> z konfiguracji aplikacji (§5.5).</summary>
    private static InjectionOptions CreateInjectionOptions(IServiceProvider services)
    {
        var settings = services.GetRequiredService<ISettingsService>().Current;
        var logger = services.GetRequiredService<ILogger<App>>();

        return new InjectionOptions
        {
            Strategy = ParseEnum(settings.InjectionStrategy, InjectionStrategy.Clipboard, logger),
            ClickToFocus = settings.InjectionClickToFocus,
            ClipboardRestoreDelayMs = settings.InjectionClipboardRestoreDelayMs,
            AppendSpace = settings.InjectionAppendSpace,
            SkipPasswordFields = settings.InjectionSkipPasswordFields,
        };
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback, Microsoft.Extensions.Logging.ILogger logger)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        logger.LogWarning(
            "Nieznana wartość '{Value}' dla {Enum} w konfiguracji — używam domyślnej {Fallback}.",
            value, typeof(TEnum).Name, fallback);
        return fallback;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _editMode?.Dispose();
        _overlay?.Dispose();
        _tray?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
