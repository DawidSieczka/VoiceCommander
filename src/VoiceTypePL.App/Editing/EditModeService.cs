using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VoiceTypePL.App.Overlay;
using VoiceTypePL.Core.Injection;
using VoiceTypePL.EditEngine;

namespace VoiceTypePL.App.Editing;

/// <summary>
/// Tryb edycji zdania (§5.6). Skrót Ctrl+Alt+E uruchamia kaskadę poziomów zaznaczenia zdania pod
/// kursorem: Poziom 3 (użytkownik sam zaznaczył), Poziom 1 (UIA — <see cref="UiaSentenceLocator"/>) i
/// Poziom 2 (heurystyka kliknięć — <see cref="ClickSentenceSelector"/>). Wynik detekcji jest cache'owany
/// per proces (<see cref="EditLevelCache"/>). Po zaznaczeniu pokazujemy podgląd „stare → nowe" w dymku i
/// włączamy „tryb podmiany" — kolejne podyktowane zdanie nadpisze zaznaczenie (wstrzykiwanie z Etapu 4).
/// Esc/odrzucenie anuluje bez zmian.
/// </summary>
public sealed class EditModeService : IDisposable
{
    private const uint VK_E = 0x45;

    private readonly UiaSentenceLocator _locator;
    private readonly ClickSentenceSelector _clickSelector;
    private readonly ISelectionController _selection;
    private readonly EditLevelCache _cache;
    private readonly OverlayService _overlay;
    private readonly ILogger<EditModeService> _logger;
    private readonly GlobalHotkey _hotkey = new();

    private SelectionHighlightWindow? _highlight;
    private DispatcherTimer? _safetyTimer;
    private bool _editing;

    public EditModeService(
        UiaSentenceLocator locator,
        ClickSentenceSelector clickSelector,
        ISelectionController selection,
        EditLevelCache cache,
        OverlayService overlay,
        ILogger<EditModeService> logger)
    {
        _locator = locator;
        _clickSelector = clickSelector;
        _selection = selection;
        _cache = cache;
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
            _logger.LogInformation("Tryb edycji gotowy — skrót Ctrl+Alt+E (Poziomy 1/2/3).");
        }
        else
        {
            _logger.LogWarning("Nie udało się zarejestrować Ctrl+Alt+E (zajęty przez inną aplikację).");
        }
    }

    private async void OnEditHotkey()
    {
        EndEdit(cancelled: true);   // wyczyść ewentualną poprzednią, niedokończoną edycję

        var (cursorX, cursorY, _) = ScreenGeometry.CursorAndMonitor();
        var (processName, isForeground) = EditTargetInfo.Describe(cursorX, cursorY);

        LocatedSentence? located = null;
        var level = EditLevel.Uia;

        // Poziom 3 (ręczny): użytkownik sam zaznaczył tekst w oknie pierwszoplanowym pod kursorem.
        // Warunek foreground chroni przed odczytem zaznaczenia z innego okna niż wskazane kursorem.
        if (isForeground)
        {
            var existing = await _selection.ReadSelectionAsync();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                located = new LocatedSentence(existing.Trim(), Array.Empty<Rect>());
                level = EditLevel.Manual;
                _logger.LogInformation("Edycja (Poziom 3 — ręczny): podmieni zaznaczenie \"{Old}\".", located.Text);
            }
        }

        // Poziomy 1/2 — kolejność sterowana cache per proces.
        if (located is null)
        {
            (located, level) = await LocateByLevelsAsync(cursorX, cursorY, _cache.Get(processName));
        }

        if (located is null || string.IsNullOrWhiteSpace(located.Text))
        {
            _logger.LogInformation("Edycja: nie udało się zaznaczyć zdania pod kursorem (Poziomy 1/2/3).");
            return;
        }

        _cache.Set(processName, level);
        _highlight!.ShowAt(located.Rectangles);
        _overlay.SetEditPreview(located.Text);
        _overlay.ReplaceMode = true;
        _editing = true;
        _safetyTimer!.Stop();
        _safetyTimer.Start();

        _logger.LogInformation(
            "Tryb edycji ({Level}): zaznaczono \"{Old}\" — podyktuj nową wersję (Enter podmienia, Esc anuluje).",
            level, located.Text);
    }

    /// <summary>Próbuje Poziomu 1 (UIA) i Poziomu 2 (kliknięcia). Cache decyduje, który spróbować pierwszy.</summary>
    private async Task<(LocatedSentence?, EditLevel)> LocateByLevelsAsync(int x, int y, EditLevel? preferred)
    {
        // Cache „Click": w tej aplikacji UIA już zawiodło — nie trać czasu, próbuj Poziomu 2 najpierw.
        if (preferred == EditLevel.Click)
        {
            var click = await _clickSelector.SelectAsync(x, y);
            if (IsUsable(click))
            {
                return (click, EditLevel.Click);
            }

            var uiaRetry = _locator.LocateAndSelect(x, y);
            return IsUsable(uiaRetry) ? (uiaRetry, EditLevel.Uia) : (null, EditLevel.Uia);
        }

        var uia = _locator.LocateAndSelect(x, y);
        if (IsUsable(uia))
        {
            return (uia, EditLevel.Uia);
        }

        var click2 = await _clickSelector.SelectAsync(x, y);
        return IsUsable(click2) ? (click2, EditLevel.Click) : (null, EditLevel.Uia);
    }

    private static bool IsUsable(LocatedSentence? sentence) =>
        sentence is not null && !string.IsNullOrWhiteSpace(sentence.Text);

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
        _overlay.SetEditPreview(null);
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
