using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.Configuration;
using VoiceTypePL.Core.History;
using VoiceTypePL.Core.Injection;
using VoiceTypePL.Core.Speech;

namespace VoiceTypePL.App.Overlay;

/// <summary>
/// Spina transkrypcję z wpisywaniem tekstu. Dwa tryby (ustawienie <c>DictationMode</c>, czytane na żywo):
/// <list type="bullet">
/// <item><b>Direct</b> (domyślny) — zdanie jest wpisywane od razu w okno z fokusem, bez dymka; dymek
/// pojawia się wyłącznie w trybie edycji (Ctrl+Alt+E), gdzie nowa wersja czeka na Enter/Esc.</item>
/// <item><b>Confirm</b> — klasyczny przepływ §5.4: każde zdanie czeka w dymku (kolejka, Enter/Esc,
/// auto-ukrywanie).</item>
/// </list>
/// Globalny hook Enter/Esc jest instalowany tylko na czas widoczności dymka (WH_KEYBOARD_LL opóźnia
/// każde naciśnięcie klawisza w systemie). Wstrzykiwania są serializowane — dwa zdania nie walczą
/// o schowek. Cała praca z UI odbywa się na wątku Dispatchera.
/// </summary>
public sealed class OverlayService : IDisposable
{
    private readonly ITranscriber _transcriber;
    private readonly ITextInjector _injector;
    private readonly SentenceRegistry _registry;
    private readonly ISettingsService _settings;
    private readonly OverlayOptions _options;
    private readonly ILogger<OverlayService> _logger;

    private readonly OverlayViewModel _viewModel = new();
    private readonly OverlaySentenceQueue _queue = new();
    private readonly GlobalKeyboardHook _hook = new();

    // Serializacja wstrzyknięć: InjectAsync zawiera opóźnienia (klik, przywrócenie schowka), więc dwa
    // równoległe wywołania na Dispatcherze mogłyby się przeplatać i pomieszać zawartość schowka.
    private readonly SemaphoreSlim _injectGate = new(1, 1);

    private Dispatcher? _dispatcher;
    private OverlayWindow? _window;
    private DispatcherTimer? _autoHideTimer;

    public OverlayService(
        ITranscriber transcriber,
        ITextInjector injector,
        SentenceRegistry registry,
        ISettingsService settings,
        OverlayOptions options,
        ILogger<OverlayService> logger)
    {
        _transcriber = transcriber;
        _injector = injector;
        _registry = registry;
        _settings = settings;
        _options = options;
        _logger = logger;
    }

    /// <summary>Zgłaszane po zatwierdzeniu zdania z dymka (i wpisaniu go) — dla trybu edycji.</summary>
    public event EventHandler<string>? SentenceConfirmed;

    /// <summary>Zgłaszane, gdy zdanie zniknęło z dymka bez wpisania (odrzucone/auto-ukryte) — dla trybu edycji.</summary>
    public event EventHandler? SentenceDismissed;

    /// <summary>
    /// Tryb edycji (§5.6): gdy true, transkrypcje idą do dymka (nawet w trybie Direct), a zatwierdzenie
    /// nadpisuje zaznaczone zdanie (bez dopinania spacji). Ustawiany przez EditModeService.
    /// </summary>
    public bool ReplaceMode { get; set; }

    /// <summary>Czy dyktowanie działa w trybie bezpośrednim (bez dymka). Czytane z ustawień na żywo.</summary>
    private bool IsDirectMode =>
        !string.Equals(_settings.Current.DictationMode, "Confirm", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ustawia (lub czyści — <c>null</c>) podgląd „stare → nowe" w dymku (§5.6). Woła EditModeService po
    /// zaznaczeniu zdania do podmiany. Bezpieczne z dowolnego wątku — przełącza na Dispatcher.
    /// </summary>
    public void SetEditPreview(string? oldText)
    {
        void Apply() => _viewModel.OldText = oldText ?? string.Empty;

        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _dispatcher.BeginInvoke(Apply);
        }
    }

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

        _transcriber.SentenceTranscribed += OnSentenceTranscribed;
        _logger.LogInformation(
            "OverlayService gotowy (tryb: {Mode}, auto-ukrywanie: {AutoHide}).",
            IsDirectMode ? "Direct" : "Confirm",
            _options.AutoHideSeconds > 0 ? $"{_options.AutoHideSeconds:F0}s" : "wyłączone");
    }

    private void OnSentenceTranscribed(object? sender, TranscribedSentence sentence)
    {
        // Zdarzenie przychodzi z wątku transkrypcji — przełącz na UI.
        _dispatcher?.BeginInvoke(() =>
        {
            if (IsDirectMode && !ReplaceMode)
            {
                _ = InjectDirectAsync(sentence.Text);
                return;
            }

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

    /// <summary>Tryb Direct: wpisz zdanie od razu w okno z fokusem — bez dymka i bez potwierdzania.</summary>
    private async Task InjectDirectAsync(string text)
    {
        try
        {
            var result = await InjectSerializedAsync(text, appendSpace: null, clickToFocus: null);
            if (result.Success)
            {
                _logger.LogInformation("Wpisano bezpośrednio: \"{Text}\".", text);
            }
            else
            {
                _logger.LogInformation("Nie wpisano ({Reason}): \"{Text}\".",
                    result.SkippedReason ?? "niepowodzenie", text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd bezpośredniego wpisywania zdania.");
        }
    }

    private async Task<InjectionResult> InjectSerializedAsync(string text, bool? appendSpace, bool? clickToFocus)
    {
        await _injectGate.WaitAsync();
        try
        {
            var result = await _injector.InjectAsync(text, appendSpace, clickToFocus);
            if (result.Success)
            {
                _registry.Record(new RegisteredSentence(
                    text, DateTimeOffset.Now, result.WindowTitle, result.ProcessName, result.ProcessId, result.Strategy));
            }

            return result;
        }
        finally
        {
            _injectGate.Release();
        }
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
        _hook.Install();                 // hook tylko na czas widoczności dymka
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
            var result = await InjectSerializedAsync(
                value,
                appendSpace: ReplaceMode ? false : null,
                clickToFocus: ReplaceMode ? false : null);
            if (result.Success)
            {
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
        // Zwolnij bieżące zdanie — użytkownik dyktuje je od nowa (kolejna transkrypcja trafi w to samo miejsce).
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
        _hook.Uninstall();
        _autoHideTimer?.Stop();
        _viewModel.PendingCount = 0;
        _viewModel.OldText = string.Empty;   // sprzątnij podgląd edycji, żeby nie wisiał przy dyktowaniu
        _window?.Hide();
    }

    private void RestartAutoHide()
    {
        // Interwał czytany z opcji przy każdym pokazaniu — zmiana w ustawieniach działa bez restartu.
        _autoHideTimer?.Stop();
        var seconds = _options.AutoHideSeconds;
        if (seconds <= 0)
        {
            return;
        }

        if (_autoHideTimer is null)
        {
            _autoHideTimer = new DispatcherTimer();
            _autoHideTimer.Tick += OnAutoHideTick;
        }

        _autoHideTimer.Interval = TimeSpan.FromSeconds(seconds);
        _autoHideTimer.Start();
    }

    public void Dispose()
    {
        _transcriber.SentenceTranscribed -= OnSentenceTranscribed;
        _autoHideTimer?.Stop();
        _hook.Dispose();
        _window?.Close();
        _injectGate.Dispose();
    }
}
