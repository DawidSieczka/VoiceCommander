using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VoiceTypePL.App.Overlay;
using VoiceTypePL.Core.Configuration;
using VoiceTypePL.Core.Injection;
using VoiceTypePL.Injection;
using VoiceTypePL.Vad;

namespace VoiceTypePL.App.Settings;

/// <summary>
/// Okno ustawień (Etap 7, §5.8). Przy otwarciu wypełnia kontrolki z <see cref="ISettingsService"/>;
/// „Zapisz" zapisuje plik konfiguracji i od razu stosuje, co się da bez restartu: tryb dyktowania
/// i czarna lista są czytane na żywo, a próg ciszy VAD, strategia wstrzykiwania, auto-ukrycie dymka
/// i autostart są wpisywane do żyjących obiektów opcji. Zmiany modelu Whispera wymagają restartu
/// (model siedzi w pamięci). Pasek poziomu sygnału odświeża timer co 100 ms z <see cref="AppState"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] ModelChoices =
        { "Tiny", "Base", "Small", "Medium", "LargeV3", "LargeV3Turbo" };

    private readonly ISettingsService _settings;
    private readonly AppState _state;
    private readonly VadOptions _vadOptions;
    private readonly InjectionOptions _injectionOptions;
    private readonly OverlayOptions _overlayOptions;
    private readonly AutostartManager _autostart;
    private readonly DispatcherTimer _signalTimer;

    public SettingsWindow(
        ISettingsService settings,
        AppState state,
        VadOptions vadOptions,
        InjectionOptions injectionOptions,
        OverlayOptions overlayOptions,
        AutostartManager autostart)
    {
        _settings = settings;
        _state = state;
        _vadOptions = vadOptions;
        _injectionOptions = injectionOptions;
        _overlayOptions = overlayOptions;
        _autostart = autostart;

        InitializeComponent();
        FillModelCombos();
        LoadFromSettings();

        _signalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _signalTimer.Tick += (_, _) => SignalLevelBar.Value = Math.Min(1.0, _state.SignalLevel * 5);
        _signalTimer.Start();
        Closed += (_, _) => _signalTimer.Stop();
    }

    private void FillModelCombos()
    {
        foreach (var model in ModelChoices)
        {
            GpuModelCombo.Items.Add(model);
            CpuModelCombo.Items.Add(model);
        }
    }

    private void LoadFromSettings()
    {
        var s = _settings.Current;

        SelectByTag(DictationModeCombo, s.DictationMode, fallback: "Direct");
        SilenceSlider.Value = Math.Clamp(s.VadMinSilenceMs, 400, 1200);

        PreferGpuCheck.IsChecked = s.WhisperPreferGpu;
        SelectModel(GpuModelCombo, s.WhisperGpuModel);
        SelectModel(CpuModelCombo, s.WhisperCpuModel);

        SelectByTag(StrategyCombo, s.InjectionStrategy, fallback: "Clipboard");
        AppendSpaceCheck.IsChecked = s.InjectionAppendSpace;

        AutoHideSlider.Value = Math.Clamp(s.OverlayAutoHideSeconds, 0, 30);

        AutostartCheck.IsChecked = s.AutostartEnabled;
        BlacklistBox.Text = string.Join(", ", s.BlacklistProcesses);
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var s = _settings.Current;

        s.DictationMode = SelectedTag(DictationModeCombo, "Direct");
        s.VadMinSilenceMs = (int)SilenceSlider.Value;
        s.WhisperPreferGpu = PreferGpuCheck.IsChecked == true;
        s.WhisperGpuModel = GpuModelCombo.SelectedItem as string ?? s.WhisperGpuModel;
        s.WhisperCpuModel = CpuModelCombo.SelectedItem as string ?? s.WhisperCpuModel;
        s.InjectionStrategy = SelectedTag(StrategyCombo, "Clipboard");
        s.InjectionAppendSpace = AppendSpaceCheck.IsChecked == true;
        s.OverlayAutoHideSeconds = (int)AutoHideSlider.Value;
        s.AutostartEnabled = AutostartCheck.IsChecked == true;
        s.BlacklistProcesses = BlacklistBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => entry.Length > 0)
            .ToList();

        _settings.Save();

        // Zastosuj na żywo to, co żyje w obiektach opcji (tryb dyktowania i czarną listę
        // konsumenci czytają bezpośrednio z ISettingsService.Current).
        _vadOptions.MinSilenceDuration = TimeSpan.FromMilliseconds(s.VadMinSilenceMs);
        _injectionOptions.Strategy = Enum.TryParse<InjectionStrategy>(s.InjectionStrategy, true, out var strategy)
            ? strategy
            : _injectionOptions.Strategy;
        _injectionOptions.AppendSpace = s.InjectionAppendSpace;
        _overlayOptions.AutoHideSeconds = s.OverlayAutoHideSeconds;
        _autostart.Apply(s.AutostartEnabled);

        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        // Odtwórz Current z pliku — kontrolki mogły nie zdążyć nic zmienić, ale Load jest tani i pewny.
        _settings.Load();
        Close();
    }

    private void OnSilenceSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SilenceValueLabel is not null)
        {
            SilenceValueLabel.Text = $"{(int)e.NewValue} ms";
        }
    }

    private void OnAutoHideSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AutoHideValueLabel is not null)
        {
            AutoHideValueLabel.Text = e.NewValue <= 0 ? "nigdy" : $"{(int)e.NewValue} s";
        }
    }

    private static void SelectByTag(ComboBox combo, string value, string fallback)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (!string.Equals(value, fallback, StringComparison.OrdinalIgnoreCase))
        {
            SelectByTag(combo, fallback, fallback);
        }
        else if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static string SelectedTag(ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static void SelectModel(ComboBox combo, string model)
    {
        var index = Array.FindIndex(ModelChoices, m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            combo.Items.Add(model);          // model spoza listy (np. wpisany ręcznie w JSON) — pokaż go
            index = combo.Items.Count - 1;
        }

        combo.SelectedIndex = index;
    }
}
