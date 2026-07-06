using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using VoiceTypePL.App.Settings;

namespace VoiceTypePL.App.Tray;

/// <summary>
/// Zarządza ikoną zasobnika i jej menu kontekstowym: „Pauza" (ręczna), „Ustawienia…" (okno z Etapu 7)
/// oraz „Zamknij". Ikona i tooltip odzwierciedlają faktyczny stan nasłuchu — w tym auto-pauzę
/// z czarnej listy (§5.8), której checkbox „Pauza" nie dotyczy.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static readonly Color ActiveAccent = Color.FromRgb(0x4C, 0xAF, 0x50); // zielony — nasłuch
    private static readonly Color PausedAccent = Color.FromRgb(0x9E, 0x9E, 0x9E); // szary — pauza

    private readonly AppState _state;
    private readonly SettingsWindowService _settingsWindow;
    private readonly ILogger<TrayIconService> _logger;
    private readonly string _iconDirectory;

    private TaskbarIcon? _icon;
    private MenuItem? _pauseItem;

    public TrayIconService(AppState state, SettingsWindowService settingsWindow, ILogger<TrayIconService> logger)
    {
        _state = state;
        _settingsWindow = settingsWindow;
        _logger = logger;
        _iconDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceTypePL", "icons");
    }

    /// <summary>Tworzy ikonę w zasobniku i podłącza menu. Wołane na wątku UI po starcie hosta.</summary>
    public void Initialize()
    {
        _pauseItem = new MenuItem
        {
            Header = "Pauza",
            IsCheckable = true,
            IsChecked = _state.IsPaused,
        };
        _pauseItem.Click += OnPauseClicked;

        var settingsItem = new MenuItem { Header = "Ustawienia…" };
        settingsItem.Click += (_, _) => _settingsWindow.Show();

        var exitItem = new MenuItem { Header = "Zamknij" };
        exitItem.Click += OnExitClicked;

        var menu = new ContextMenu();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _icon = new TaskbarIcon
        {
            IconSource = IconFor(_state.IsEffectivelyPaused),
            ToolTipText = TooltipFor(),
            ContextMenu = menu,
        };
        _icon.ForceCreate();

        _state.PausedChanged += OnPausedChanged;
        ApplyPausedVisuals();

        _logger.LogInformation("Ikona zasobnika zainicjalizowana");
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        // Aktualizacja _state podniesie PausedChanged → ApplyPausedVisuals zsynchronizuje UI.
        _state.IsPaused = _pauseItem!.IsChecked;
    }

    private void OnPausedChanged(object? sender, bool paused)
    {
        // Zdarzenie może przyjść z timera czarnej listy — wizualizacja zawsze na wątku UI ikony.
        Application.Current?.Dispatcher.BeginInvoke(ApplyPausedVisuals);
    }

    private void ApplyPausedVisuals()
    {
        if (_icon is null)
        {
            return;
        }

        _pauseItem!.IsChecked = _state.IsPaused;      // checkbox = tylko pauza ręczna
        _icon.IconSource = IconFor(_state.IsEffectivelyPaused);
        _icon.ToolTipText = TooltipFor();
        _logger.LogInformation("Stan nasłuchu: {Tooltip}", _icon.ToolTipText);
    }

    private ImageSource IconFor(bool paused)
    {
        var accent = paused ? PausedAccent : ActiveAccent;
        var path = Path.Combine(_iconDirectory, paused ? "tray-paused.ico" : "tray-active.ico");
        return TrayIconFactory.CreateAndSave(accent, path);
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Zamknięcie aplikacji z menu zasobnika");
        Application.Current.Shutdown();
    }

    private string TooltipFor() => _state switch
    {
        { IsPaused: true } => "VoiceType PL — pauza",
        { IsAutoPaused: true } => "VoiceType PL — pauza (czarna lista)",
        _ => "VoiceType PL — nasłuch",
    };

    public void Dispose()
    {
        _state.PausedChanged -= OnPausedChanged;
        _icon?.Dispose();
        _icon = null;
    }
}
