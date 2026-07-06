using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace VoiceTypePL.App.Settings;

/// <summary>
/// Autostart z Windows (§5.8): wpis w <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
/// wskazujący na bieżący plik wykonywalny. Bez elevacji (klucz per użytkownik). Operacje odporne
/// na błędy — niepowodzenie loguje się i nie wywala aplikacji.
/// </summary>
public sealed class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoiceTypePL";

    private readonly ILogger<AutostartManager> _logger;

    public AutostartManager(ILogger<AutostartManager> logger)
    {
        _logger = logger;
    }

    /// <summary>Czy wpis autostartu istnieje i wskazuje na bieżący plik wykonywalny.</summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value)
                   && string.Equals(value.Trim('"'), ExecutablePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się odczytać wpisu autostartu.");
            return false;
        }
    }

    /// <summary>Zapisuje lub usuwa wpis autostartu zgodnie z <paramref name="enabled"/>.</summary>
    public void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath()}\"");
                _logger.LogInformation("Autostart włączony ({Path}).", ExecutablePath());
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName);
                _logger.LogInformation("Autostart wyłączony.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się zmienić wpisu autostartu.");
        }
    }

    private static string ExecutablePath() => Environment.ProcessPath ?? string.Empty;
}
