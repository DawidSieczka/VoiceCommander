using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VoiceTypePL.App.Overlay;
using VoiceTypePL.EditEngine;

namespace VoiceTypePL.App.Editing;

/// <summary>
/// Tryb edycji zdania (§5.6, Poziom 1). Skrót Ctrl+Alt+E: znajdź i zaznacz zdanie pod kursorem (UIA),
/// pokaż ramkę podświetlenia i włącz „tryb podmiany" w dymku — kolejne podyktowane zdanie po
/// zatwierdzeniu nadpisze zaznaczenie (wstrzykiwanie z Etapu 4). Esc/odrzucenie anuluje bez zmian.
/// </summary>
public sealed class EditModeService : IDisposable
{
    private const uint VK_E = 0x45;

    private readonly UiaSentenceLocator _locator;
    private readonly OverlayService _overlay;
    private readonly ILogger<EditModeService> _logger;
    private readonly GlobalHotkey _hotkey = new();

    private SelectionHighlightWindow? _highlight;
    private DispatcherTimer? _safetyTimer;
    private bool _editing;

    public EditModeService(UiaSentenceLocator locator, OverlayService overlay, ILogger<EditModeService> logger)
    {
        _locator = locator;
        _overlay = overlay;
        _logger = logger;
    }

    /// <summary>Tworzy okno podświetlenia i rejestruje skrót. Wołane z wątku UI.</summary>
    public void Initialize()
    {
        _highlight = new SelectionHighlightWindow();

        _overlay.SentenceConfirmed += OnSentenceConfirmed;
        _overlay.SentenceDismissed += OnSentenceDismissed;

        // Bezpiecznik: gdyby użytkownik nic nie podyktował, po chwili sprzątamy podświetlenie.
        _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _safetyTimer.Tick += (_, _) => EndEdit(cancelled: true);

        _hotkey.Pressed += OnEditHotkey;
        if (_hotkey.Register(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT, VK_E))
        {
            _logger.LogInformation("Tryb edycji gotowy — skrót Ctrl+Alt+E.");
        }
        else
        {
            _logger.LogWarning("Nie udało się zarejestrować Ctrl+Alt+E (zajęty przez inną aplikację).");
        }
    }

    private void OnEditHotkey()
    {
        EndEdit(cancelled: true);   // wyczyść ewentualną poprzednią, niedokończoną edycję

        var (cursorX, cursorY, _) = ScreenGeometry.CursorAndMonitor();
        var located = _locator.LocateAndSelect(cursorX, cursorY);
        if (located is null || string.IsNullOrWhiteSpace(located.Text))
        {
            _logger.LogInformation(
                "Edycja: pod kursorem brak zdania dostępnego przez UIA (Poziom 1). Poziomy 2/3 dojdą w Etapie 6.");
            return;
        }

        _highlight!.ShowAt(located.Rectangles);
        _overlay.ReplaceMode = true;
        _editing = true;
        _safetyTimer!.Stop();
        _safetyTimer.Start();

        _logger.LogInformation(
            "Tryb edycji: zaznaczono \"{Old}\" — podyktuj nową wersję (Enter podmienia, Esc anuluje).",
            located.Text);
    }

    private void OnSentenceConfirmed(object? sender, string newText)
    {
        if (!_editing)
        {
            return;
        }

        _logger.LogInformation("Edycja: podmieniono zaznaczone zdanie na \"{New}\".", newText);
        EndEdit(cancelled: false);
    }

    private void OnSentenceDismissed(object? sender, EventArgs e)
    {
        if (!_editing)
        {
            return;
        }

        _logger.LogInformation("Edycja: anulowano podmianę (zaznaczenie bez zmian).");
        EndEdit(cancelled: true);
    }

    private void EndEdit(bool cancelled)
    {
        _editing = false;
        _overlay.ReplaceMode = false;
        _safetyTimer?.Stop();
        _highlight?.HideFrame();
    }

    public void Dispose()
    {
        _overlay.SentenceConfirmed -= OnSentenceConfirmed;
        _overlay.SentenceDismissed -= OnSentenceDismissed;
        _safetyTimer?.Stop();
        _hotkey.Dispose();
        _highlight?.Close();
    }
}
