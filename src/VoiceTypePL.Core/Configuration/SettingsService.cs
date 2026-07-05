using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.Models;

namespace VoiceTypePL.Core.Configuration;

/// <summary>
/// Domyślna implementacja <see cref="ISettingsService"/> oparta o System.Text.Json.
/// Plik trzymany w %AppData%\VoiceTypePL\config.json. Wszystkie operacje I/O są
/// odporne na błędy — awaria odczytu/zapisu nie wywala aplikacji, tylko loguje i wraca
/// do wartości domyślnych.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        ConfigFilePath = BuildConfigPath();
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public string ConfigFilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                _logger.LogInformation("Brak pliku konfiguracji — tworzę domyślny: {Path}", ConfigFilePath);
                Current = new AppSettings();
                Save();
                return Current;
            }

            var json = File.ReadAllText(ConfigFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
            {
                _logger.LogWarning("Plik konfiguracji pusty lub niepoprawny — używam wartości domyślnych: {Path}", ConfigFilePath);
                settings = new AppSettings();
            }

            Current = settings;
            _logger.LogInformation("Wczytano konfigurację z {Path}", ConfigFilePath);
            return Current;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się wczytać konfiguracji z {Path} — używam wartości domyślnych", ConfigFilePath);
            Current = new AppSettings();
            return Current;
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigFilePath)!;
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
            _logger.LogDebug("Zapisano konfigurację do {Path}", ConfigFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się zapisać konfiguracji do {Path}", ConfigFilePath);
        }
    }

    private static string BuildConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "VoiceTypePL", "config.json");
    }
}
