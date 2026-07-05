using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.History;
using VoiceTypePL.Core.Injection;
using VoiceTypePL.Core.Speech;

namespace VoiceTypePL.App.Overlay;

/// <summary>
/// Spina dymek z resztą aplikacji (§5.4): subskrybuje transkrypcje, kolejkuje je (jedno pokazane,
/// reszta czeka), pokazuje okno przy kursorze bez kradzieży fokusa, obsługuje globalny Enter/Esc oraz
/// auto-ukrywanie. Zatwierdzenie zdania zgłasza przez <see cref="SentenceConfirmed"/> — właściwe
/// „wpisanie" tekstu podłączy Etap 4 (wstrzykiwanie). Cała praca z UI odbywa się na wątku Dispatchera.
/// </summary>
public sealed class OverlayService : IDisposable
{
    private readonly ITranscriber _transcriber;
    private readonly ITextInjector _injector;
    private readonly SentenceRegistry _registry;
    private readonly OverlayOptions _options;
    private readonly ILogger<OverlayService> _logger;

    private readonly OverlayViewModel _viewModel = new();
    private readonly OverlaySentenceQueue _queue = new();
    private readonly GlobalKeyboardHook _hook = new();

    private Dispatcher? _dispatcher;
    private OverlayWindow? _window;
    private DispatcherTimer? _autoHideTimer;

    public OverlayService(
        ITranscriber transcriber,
        ITextInjector injector,
        SentenceRegistry registry,
        OverlayOptions options,
        ILogger<OverlayService> logger)
    {
        _transcriber = transcriber;
        _injector = injector;
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    /// <summary>Zgłaszane po zatwierdzeniu zdania (i wpisaniu go).</summary>
    public event EventHandler<string>? SentenceConfirmed;

    /// <summary>Zgłaszane, gdy zdanie zniknęło bez wpisania (odrzucone/auto-ukryte) — dla trybu edycji.</summary>
    public event EventHandler? SentenceDismissed;

    /// <summary>
    /// Tryb edycji (§5.6): gdy true, zatwierdzenie nadpisuje zaznaczone zdanie (bez dopinania spacji).
    /// Ustawiany przez EditModeService po zaznaczeniu zdania do podmiany.
    /// </summary>
    public bool ReplaceMode { get; set; }

    /// <summary>Tworzy okno i hooki. Musi być wołane z wątku UI (jak inicjalizacja zasobnika).</summary>
    public void Initialize()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        _window = new OverlayWindow(_viewModel);
        _window.ConfirmRequested += OnConfirmRequested;
        _window.RejectRequested += OnRejectRequested;
        _window.RerecordRequested += OnRerecordRequested;

        _hook.EnterPressed += () => _window?.Confirm();
        _hook.EscapePressed += () => _window?.Reject();
        _hook.Install();

        if (_options.AutoHideSeconds > 0)
        {
            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_options.AutoHideSeconds) };
            _autoHideTimer.Tick += OnAutoHideTick;
        }

        _transcriber.SentenceTranscribed += OnSentenceTranscribed;
        _logger.LogInformation("OverlayService gotowy (auto-ukrywanie: {AutoHide}).",
            _options.AutoHideSeconds > 0 ? $"{_options.AutoHideSeconds:F0}s" : "wyłączone");
    }

    private void OnSentenceTranscribed(object? sender, TranscribedSentence sentence)
    {
        // Zdarzenie przychodzi z wątku transkrypcji — przełącz na UI.
        _dispatcher?.BeginInvoke(() =>
        {
            var becameCurrent = _queue.Enqueue(sentence);
            if (becameCurrent)
            {
                ShowCurrent();
            }
            else
            {
                _viewModel.PendingCount = _queue.PendingCount;
                _logger.LogInformation("Zdanie w kolejce (oczekujących: {Count}).", _queue.PendingCount);
            }
        });
    }

    private void ShowCurrent()
    {
        var current = _queue.Current;
        if (current is null || _window is null)
        {
            return;
        }

        _viewModel.Text = current.Text;
        _viewModel.PendingCount = _queue.PendingCount;
        _window.ShowNoActivate();
        _hook.IsEnabled = true;
        RestartAutoHide();

        _logger.LogInformation("Dymek: \"{Text}\" (oczekujących: {Pending}).", current.Text, _queue.PendingCount);
    }

    private async void OnConfirmRequested(object? sender, string text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? _queue.Current?.Text ?? string.Empty : text;

        // Schowaj dymek przed wpisaniem — żeby nie zasłaniał punktu kliknięcia/wklejenia przy kursorze.
        _hook.IsEnabled = false;
        _autoHideTimer?.Stop();
        _window?.Hide();

        try
        {
            var result = await _injector.InjectAsync(
                value,
                appendSpace: ReplaceMode ? false : null,
                clickToFocus: ReplaceMode ? false : null);
            if (result.Success)
            {
                _registry.Record(new RegisteredSentence(
                    value, DateTimeOffset.Now, result.WindowTitle, result.ProcessName, result.ProcessId, result.Strategy));
                _logger.LogInformation("Zatwierdzono i wpisano: \"{Text}\".", value);
            }
            else
            {
                _logger.LogInformation("Zatwierdzono, ale nie wpisano ({Reason}).",
                    result.SkippedReason ?? "niepowodzenie");
            }

            SentenceConfirmed?.Invoke(this, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas wstrzykiwania zatwierdzonego zdania.");
        }
        finally
        {
            AdvanceOrHide();
        }
    }

    private void OnRejectRequested(object? sender, EventArgs e)
    {
        _logger.LogInformation("Odrzucono zdanie.");
        SentenceDismissed?.Invoke(this, EventArgs.Empty);
        AdvanceOrHide();
    }

    private void OnRerecordRequested(object? sender, EventArgs e)
    {
        // Pełne ponowne dyktowanie „w miejscu" dopracujemy w Etapie 7; na razie zwolnij slot.
        _logger.LogInformation("Nagraj ponownie — zwalniam bieżące zdanie.");
        SentenceDismissed?.Invoke(this, EventArgs.Empty);
        AdvanceOrHide();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        _logger.LogInformation("Auto-ukrycie dymka po bezczynności.");
        SentenceDismissed?.Invoke(this, EventArgs.Empty);
        AdvanceOrHide();
    }

    private void AdvanceOrHide()
    {
        var next = _queue.Advance();
        if (next is not null)
        {
            ShowCurrent();
        }
        else
        {
            Hide();
        }
    }

    private void Hide()
    {
        _hook.IsEnabled = false;
        _autoHideTimer?.Stop();
        _viewModel.PendingCount = 0;
        _window?.Hide();
    }

    private void RestartAutoHide()
    {
        if (_autoHideTimer is null)
        {
            return;
        }

        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    public void Dispose()
    {
        _transcriber.SentenceTranscribed -= OnSentenceTranscribed;
        _autoHideTimer?.Stop();
        _hook.Dispose();
        _window?.Close();
    }
}
