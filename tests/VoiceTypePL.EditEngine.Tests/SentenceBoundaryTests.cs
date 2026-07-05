using VoiceTypePL.EditEngine;

namespace VoiceTypePL.EditEngine.Tests;

/// <summary>Testy ekspansji do zdania (§5.6, Poziom 1) — czysto, bez UIA.</summary>
public sealed class SentenceBoundaryTests
{
    private static string SentenceAt(string text, int index)
    {
        var (start, length) = SentenceBoundary.Expand(text, index);
        return text.Substring(start, length);
    }

    [Fact]
    public void PicksMiddleSentence()
    {
        const string text = "Ala ma kota. Ma też psa. Koniec.";
        Assert.Equal("Ma też psa.", SentenceAt(text, 16)); // 't' w "też"
    }

    [Fact]
    public void PicksFirstSentence()
    {
        const string text = "Ala ma kota. Ma też psa.";
        Assert.Equal("Ala ma kota.", SentenceAt(text, 1));
    }

    [Fact]
    public void PicksLastSentence()
    {
        const string text = "Pierwsze zdanie. Drugie zdanie!";
        Assert.Equal("Drugie zdanie!", SentenceAt(text, 20));
    }

    [Fact]
    public void NoPunctuation_ReturnsWholeText()
    {
        const string text = "bez kropki na końcu";
        Assert.Equal(text, SentenceAt(text, 3));
    }

    [Fact]
    public void SplitsOnNewline_WithoutIncludingIt()
    {
        const string text = "linia jeden\nlinia dwa";
        Assert.Equal("linia dwa", SentenceAt(text, 14));
        Assert.Equal("linia jeden", SentenceAt(text, 2));
    }

    [Fact]
    public void TrimsLeadingWhitespace()
    {
        const string text = "Pierwsze.   Drugie zdanie.";
        Assert.Equal("Drugie zdanie.", SentenceAt(text, 14));
    }

    [Fact]
    public void IncludesQuestionAndExclamation()
    {
        Assert.Equal("Serio?", SentenceAt("No i co. Serio? Tak!", 10));
        Assert.Equal("Tak!", SentenceAt("No i co. Serio? Tak!", 16));
    }

    [Fact]
    public void RespectsMaxChars()
    {
        var text = new string('a', 1000);   // brak interpunkcji
        var (_, length) = SentenceBoundary.Expand(text, 500, maxChars: 500);
        Assert.True(length <= 500, $"Długość {length} przekroczyła limit 500.");
    }

    [Theory]
    [InlineData("")]
    public void EmptyText_ReturnsEmpty(string text)
    {
        var (start, length) = SentenceBoundary.Expand(text, 0);
        Assert.Equal(0, start);
        Assert.Equal(0, length);
    }
}
