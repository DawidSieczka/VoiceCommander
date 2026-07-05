using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;

namespace VoiceTypePL.App.Tray;

/// <summary>
/// Zarządza ikoną zasobnika i jej menu kontekstowym.
/// Etap 0 — szkielet menu: „Pauza" (przełącznik stanu w pamięci), „Ustawienia…" (wyłączone,
/// okno powstanie w Etapie 7) oraz „Zamknij". Ikona i tooltip odzwierciedlają stan pauzy.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static readonly Color ActiveAccent = Color.FromRgb(0x4C, 0xAF, 0x50); // zielony — nasłuch
    private static readonly Color PausedAccent = Color.FromRgb(0x9E, 0x9E, 0x9E); // szary — pauza

    private readonly AppState _state;
    private readonly ILogger<TrayIconService> _logger;
    private readonly string _iconDirectory;

    private TaskbarIcon? _icon;
    private MenuItem? _pauseItem;

    public TrayIconService(AppState state, ILogger<TrayIconService> logger)
    {
        _state = state;
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

        // „Ustawienia" celowo wyłączone — pełne okno ustawień powstanie w Etapie 7.
        var settingsItem = new MenuItem { Header = "Ustawienia…", IsEnabled = false };

        var exitItem = new MenuItem { Header = "Zamknij" };
        exitItem.Click += OnExitClicked;

        var menu = new ContextMenu();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _icon = new TaskbarIcon
        {
            IconSource = IconFor(_state.IsPaused),
            ToolTipText = TooltipFor(_state.IsPaused),
            ContextMenu = menu,
        };
        _icon.ForceCreate();

        _state.PausedChanged += OnPausedChanged;
        ApplyPausedVisuals(_state.IsPaused);

        _logger.LogInformation("Ikona zasobnika zainicjalizowana");
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        // Aktualizacja _state podniesie PausedChanged → ApplyPausedVisuals zsynchronizuje UI.
        _state.IsPaused = _pauseItem!.IsChecked;
    }

    private void OnPausedChanged(object? sender, bool paused) => ApplyPausedVisuals(paused);

    private void ApplyPausedVisuals(bool paused)
    {
        if (_icon is null)
        {
            return;
        }

        _pauseItem!.IsChecked = paused;
        _icon.IconSource = IconFor(paused);
        _icon.ToolTipText = TooltipFor(paused);
        _logger.LogInformation("Stan pauzy: {Paused}", paused);
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

    private static string TooltipFor(bool paused)
        => paused ? "VoiceType PL — pauza" : "VoiceType PL — nasłuch";

    public void Dispose()
    {
        _state.PausedChanged -= OnPausedChanged;
        _icon?.Dispose();
        _icon = null;
    }
}
