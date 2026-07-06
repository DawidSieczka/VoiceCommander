using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.Injection;

namespace VoiceTypePL.EditEngine;

/// <summary>
/// Poziom 2 edycji (§5.6): dla aplikacji bez UIA zaznacza zdanie pod kursorem klikiem + klawiaturą.
/// Kroki: pojedynczy klik ustawia karetkę → <c>Shift+Home</c> mierzy offset karetki w linii →
/// <c>Home</c>+<c>Shift+End</c> czyta całą linię → <see cref="SentenceBoundary"/> wyznacza zakres zdania,
/// który odtwarzamy klawiaturą (<c>Home</c> → Right×start → Shift+Right×length). Gdy linia nie ma
/// interpunkcji, <see cref="SentenceBoundary"/> zwraca całą linię — to naturalny „wariant uproszczony"
/// (podmiana całej linii). Rezygnacja z rozpoznawania syntetycznego dwukliku/trzykliku czyni to pewnym.
/// Mechanika przez <see cref="ISelectionController"/> — dzięki temu logika jest testowalna na atrapie.
/// </summary>
public sealed class ClickSentenceSelector
{
    private readonly ISelectionController _controller;
    private readonly ILogger<ClickSentenceSelector>? _logger;

    public ClickSentenceSelector(ISelectionController controller, ILogger<ClickSentenceSelector>? logger = null)
    {
        _controller = controller;
        _logger = logger;
    }

    /// <summary>
    /// Zaznacza zdanie pod punktem ekranu (px) i zwraca jego tekst. Zwraca <c>null</c>, gdy pod kursorem
    /// nie ma tekstu do edycji. Zwrócone <see cref="LocatedSentence.Rectangles"/> są puste — wizualnym
    /// wskaźnikiem jest natywne zaznaczenie w aplikacji docelowej.
    /// </summary>
    public async Task<LocatedSentence?> SelectAsync(int screenX, int screenY)
    {
        // Klik ustawia karetkę; Shift+Home zaznacza tekst od początku linii do karetki — jego długość to
        // offset kliknięcia w linii.
        _controller.Click(screenX, screenY);
        _controller.ExtendToLineStart();
        var head = await _controller.ReadSelectionAsync().ConfigureAwait(true);
        var clickOffset = head?.Length ?? 0;

        // Home + Shift+End zaznacza i pozwala odczytać całą linię/akapit.
        _controller.MoveToLineStart();
        _controller.ExtendToLineEnd();
        var line = await _controller.ReadSelectionAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(line))
        {
            _logger?.LogInformation("Poziom 2: pod kursorem brak tekstu do zaznaczenia.");
            return null;
        }

        var (start, length) = SentenceBoundary.Expand(line, clickOffset);
        if (length == 0)
        {
            _logger?.LogInformation("Poziom 2: nie wyznaczono zdania w linii \"{Line}\".", line);
            return null;
        }

        // Odtwórz dokładnie zakres zdania: początek linii → Right×start → Shift+Right×length.
        _controller.MoveToLineStart();
        _controller.MoveCaretRight(start);
        _controller.ExtendRight(length);

        var sentence = line.Substring(start, length).TrimEnd('\r', '\n');
        _logger?.LogInformation("Poziom 2: zaznaczono zdanie \"{Text}\".", sentence);
        return new LocatedSentence(sentence, Array.Empty<System.Windows.Rect>());
    }
}
