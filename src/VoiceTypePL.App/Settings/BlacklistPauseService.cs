using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.Configuration;

namespace VoiceTypePL.App.Settings;

/// <summary>
/// Auto-pauza z czarnej listy (§5.8): co sekundę sprawdza proces okna pierwszoplanowego i ustawia
/// <see cref="AppState.IsAutoPaused"/>, gdy jest on na liście z ustawień (czytanej na żywo).
/// Nazwa procesu jest cache'owana per PID — <c>Process.GetProcessById</c> nie jest wołane co tyknięcie,
/// gdy użytkownik siedzi w jednym oknie.
/// </summary>
public sealed class BlacklistPauseService : IDisposable
{
    private readonly AppState _state;
    private readonly ISettingsService _settings;
    private readonly ILogger<BlacklistPauseService> _logger;

    private DispatcherTimer? _timer;
    private uint _lastPid;
    private string? _lastProcessName;

    public BlacklistPauseService(AppState state, ISettingsService settings, ILogger<BlacklistPauseService> logger)
    {
        _state = state;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Startuje monitor okna pierwszoplanowego. Wołane z wątku UI.</summary>
    public void Initialize()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => CheckForeground();
        _timer.Start();
        _logger.LogInformation("Czarna lista aktywna ({Count} wpisów).", _settings.Current.BlacklistProcesses.Count);
    }

    private void CheckForeground()
    {
        var blacklist = _settings.Current.BlacklistProcesses;
        if (blacklist.Count == 0)
        {
            _state.IsAutoPaused = false;
            return;
        }

        var blacklisted = BlacklistMatcher.IsBlacklisted(ForegroundProcessName(), blacklist);
        if (blacklisted != _state.IsAutoPaused)
        {
            _logger.LogInformation(
                blacklisted
                    ? "Auto-pauza: aplikacja pierwszoplanowa ({Proc}) jest na czarnej liście."
                    : "Koniec auto-pauzy: aplikacja z czarnej listy nieaktywna.",
                _lastProcessName);
        }

        _state.IsAutoPaused = blacklisted;
    }

    private string? ForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        if (pid != _lastPid)
        {
            _lastPid = pid;
            try
            {
                _lastProcessName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                _lastProcessName = null;   // proces mógł zniknąć między odczytami
            }
        }

        return _lastProcessName;
    }

    public void Dispose() => _timer?.Stop();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
