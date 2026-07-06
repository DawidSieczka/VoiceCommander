using VoiceTypePL.Core.Injection;
using VoiceTypePL.EditEngine;

namespace VoiceTypePL.EditEngine.Tests;

/// <summary>
/// Testy orkiestracji Poziomu 2 (§5.6, Etap 6) na atrapie <see cref="ISelectionController"/> — bez Win32.
/// Atrapa skryptuje dwa odczyty (Shift+Home → offset karetki, Home+Shift+End → cała linia) i nagrywa
/// ruchy karetki; sprawdzamy, że selektor wyznacza właściwy zakres zdania i steruje właściwymi licznikami.
/// </summary>
public sealed class ClickSentenceSelectorTests
{
    private sealed class StubController : ISelectionController
    {
        private readonly Queue<string?> _reads;

        /// <param name="reads">Kolejne wyniki ReadSelectionAsync: [0] = tekst od początku linii do karetki, [1] = cała linia.</param>
        public StubController(params string?[] reads) => _reads = new Queue<string?>(reads);

        public (int X, int Y)? Clicked { get; private set; }
        public int MoveToLineStartCount { get; private set; }
        public int? MoveCaretRightSteps { get; private set; }
        public int? ExtendRightSteps { get; private set; }

        public Task<string?> ReadSelectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_reads.Count > 0 ? _reads.Dequeue() : null);

        public void Click(int screenX, int screenY) => Clicked = (screenX, screenY);

        public void MoveToLineStart() => MoveToLineStartCount++;

        public void ExtendToLineStart() { }

        public void ExtendToLineEnd() { }

        public void MoveCaretRight(int steps) => MoveCaretRightSteps = steps;

        public void ExtendRight(int steps) => ExtendRightSteps = steps;
    }

    [Fact]
    public async Task SelectsSentence_AroundCaretOffset()
    {
        // Karetka wewnątrz "pies": offset 17; cała linia z trzema zdaniami.
        var stub = new StubController("Ala ma kota. Ma p", "Ala ma kota. Ma pies i kot. Koniec zdania.");
        var selector = new ClickSentenceSelector(stub);

        var result = await selector.SelectAsync(100, 200);

        Assert.NotNull(result);
        Assert.Equal("Ma pies i kot.", result!.Text);
        Assert.Empty(result.Rectangles);
        Assert.Equal((100, 200), stub.Clicked);
        // Zdanie zaczyna się na indeksie 13 i ma 14 znaków.
        Assert.Equal(13, stub.MoveCaretRightSteps);
        Assert.Equal(14, stub.ExtendRightSteps);
    }

    [Fact]
    public async Task NoPunctuation_SelectsWholeLine()
    {
        // Brak interpunkcji → SentenceBoundary zwraca całą linię (naturalny wariant uproszczony).
        var stub = new StubController("cała ta ", "cała ta linia bez kropki");
        var selector = new ClickSentenceSelector(stub);

        var result = await selector.SelectAsync(0, 0);

        Assert.Equal("cała ta linia bez kropki", result!.Text);
        Assert.Equal(0, stub.MoveCaretRightSteps);      // od początku linii
        Assert.Equal("cała ta linia bez kropki".Length, stub.ExtendRightSteps);
    }

    [Fact]
    public async Task CaretAtLineStart_NullHead_TreatedAsOffsetZero()
    {
        // Shift+Home nic nie zaznaczyło (karetka na początku) → offset 0 → pierwsze zdanie.
        var stub = new StubController(null, "Pierwsze zdanie. Drugie.");
        var selector = new ClickSentenceSelector(stub);

        var result = await selector.SelectAsync(0, 0);

        Assert.Equal("Pierwsze zdanie.", result!.Text);
    }

    [Fact]
    public async Task NoText_ReturnsNull()
    {
        var stub = new StubController(null, null);   // brak zaznaczenia i pusta linia
        var selector = new ClickSentenceSelector(stub);

        Assert.Null(await selector.SelectAsync(0, 0));
    }

    [Fact]
    public async Task TrimsTrailingNewline()
    {
        var stub = new StubController("", "linia z ogonem\r\n");
        var selector = new ClickSentenceSelector(stub);

        var result = await selector.SelectAsync(0, 0);

        Assert.Equal("linia z ogonem", result!.Text);
    }
}
