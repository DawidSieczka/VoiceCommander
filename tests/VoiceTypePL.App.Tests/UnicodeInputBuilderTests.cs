using VoiceTypePL.Injection;

namespace VoiceTypePL.App.Tests;

/// <summary>Testy budowania sekwencji Unicode SendInput (§5.5) — pary down/up, polskie znaki, emoji.</summary>
public sealed class UnicodeInputBuilderTests
{
    [Fact]
    public void Build_Empty_ReturnsEmpty()
    {
        Assert.Empty(UnicodeInputBuilder.Build(string.Empty));
    }

    [Fact]
    public void Build_SingleChar_ProducesDownThenUp()
    {
        var strokes = UnicodeInputBuilder.Build("A");

        Assert.Equal(2, strokes.Count);
        Assert.Equal(new UnicodeKeyStroke(0x41, KeyUp: false), strokes[0]);
        Assert.Equal(new UnicodeKeyStroke(0x41, KeyUp: true), strokes[1]);
    }

    [Fact]
    public void Build_PolishLetter_UsesCodePoint()
    {
        // 'ł' = U+0142
        var strokes = UnicodeInputBuilder.Build("ł");

        Assert.Equal(0x0142, strokes[0].CodeUnit);
        Assert.False(strokes[0].KeyUp);
        Assert.True(strokes[1].KeyUp);
    }

    [Fact]
    public void Build_ProducesTwoStrokesPerCodeUnit()
    {
        var text = "zażółć";
        var strokes = UnicodeInputBuilder.Build(text);
        Assert.Equal(text.Length * 2, strokes.Count);
    }

    [Fact]
    public void Build_SurrogatePair_SplitsIntoTwoCodeUnits()
    {
        // 😀 = U+1F600 → para zastępcza D83D DE00 (2 jednostki UTF-16 → 4 zdarzenia).
        var strokes = UnicodeInputBuilder.Build("😀");

        Assert.Equal(4, strokes.Count);
        Assert.Equal(0xD83D, strokes[0].CodeUnit);
        Assert.Equal(0xDE00, strokes[2].CodeUnit);
    }
}
