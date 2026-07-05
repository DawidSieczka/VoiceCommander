using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using VoiceTypePL.App.Tray;
using VoiceTypePL.Audio;
using VoiceTypePL.Core.Audio;
using VoiceTypePL.Core.Configuration;
using VoiceTypePL.Core.Speech;
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
