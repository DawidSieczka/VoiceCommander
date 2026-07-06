using Microsoft.Extensions.Logging;
using VoiceTypePL.App.Overlay;
using VoiceTypePL.Core.Configuration;
using VoiceTypePL.Injection;
using VoiceTypePL.Vad;

namespace VoiceTypePL.App.Settings;

/// <summary>
/// Pojedyncza instancja okna ustawień: „Ustawienia…" z zasobnika otwiera okno albo — gdy już jest
/// otwarte — tylko je aktywuje. Wołane z wątku UI.
/// </summary>
public sealed class SettingsWindowService
{
    private readonly ISettingsService _settings;
    private readonly AppState _state;
    private readonly VadOptions _vadOptions;
    private readonly InjectionOptions _injectionOptions;
    private readonly OverlayOptions _overlayOptions;
    private readonly AutostartManager _autostart;
    private readonly ILogger<SettingsWindowService> _logger;

    private SettingsWindow? _window;

    public SettingsWindowService(
        ISettingsService settings,
        AppState state,
        VadOptions vadOptions,
        InjectionOptions injectionOptions,
        OverlayOptions overlayOptions,
        AutostartManager autostart,
        ILogger<SettingsWindowService> logger)
    {
        _settings = settings;
        _state = state;
        _vadOptions = vadOptions;
        _injectionOptions = injectionOptions;
        _overlayOptions = overlayOptions;
        _autostart = autostart;
        _logger = logger;
    }

    public void Show()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _window = new SettingsWindow(_settings, _state, _vadOptions, _injectionOptions, _overlayOptions, _autostart);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        _logger.LogInformation("Otwarto okno ustawień.");
    }
}
