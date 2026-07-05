using VoiceTypePL.Stt;

namespace VoiceTypePL.Stt.Tests;

/// <summary>
/// Deterministyczne testy post-processingu transkrypcji (§5.3) — bez modelu i sieci.
/// Pokrywają: trim/scalanie spacji, kapitalizację, dopięcie interpunkcji, filtr halucynacji
/// (znane frazy PL, progi no_speech / pewności).
/// </summary>
public sealed class TranscriptPostProcessorTests
{
    private readonly TranscriptPostProcessor _sut = new();

    [Fact]
    public void Process_TrimsAndCapitalizes_AndAddsPeriod()
    {
        var result = _sut.Process("  to jest zdanie testowe  ");
        Assert.Equal("To jest zdanie testowe.", result);
    }

    [Fact]
    public void Process_CollapsesInternalWhitespace()
    {
        var result = _sut.Process("ala   ma\t kota");
        Assert.Equal("Ala ma kota.", result);
    }

    [Theory]
    [InlineData("Czy to działa?", "Czy to działa?")]
    [InlineData("Uwaga!", "Uwaga!")]
    [InlineData("No i…", "No i…")]
    [InlineData("Wynik:", "Wynik:")]
    public void Process_KeepsExistingTerminalPunctuation(string input, string expected)
    {
        Assert.Equal(expected, _sut.Process(input));
    }

    [Fact]
    public void Process_PreservesPolishDiacritics()
    {
        var result = _sut.Process("zażółć gęślą jaźń");
        Assert.Equal("Zażółć gęślą jaźń.", result);
    }

    [Fact]
    public void Process_DoesNotDoubleCapitalOrPunctuation()
    {
        var result = _sut.Process("To już poprawne zdanie.");
        Assert.Equal("To już poprawne zdanie.", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Process_ReturnsNull_ForEmptyInput(string? input)
    {
        Assert.Null(_sut.Process(input));
    }

    [Theory]
    [InlineData("Napisy stworzone przez społeczność Amara.org")]
    [InlineData("napisy: Amara.org")]
    [InlineData("Dziękuję za uwagę")]
    [InlineData("Zapraszam do subskrypcji")]
    public void Process_RejectsKnownHallucinations(string input)
    {
        Assert.Null(_sut.Process(input));
    }

    [Fact]
    public void Process_HallucinationMatch_IgnoresDiacriticsAndCase()
    {
        // Bez polskich znaków i inna wielkość liter — nadal ma trafić w filtr.
        Assert.True(_sut.IsHallucination("DZIEKUJE ZA UWAGE"));
        Assert.True(_sut.IsHallucination("napisy stworzone przez spolecznosc amara.org"));
    }

    [Fact]
    public void Process_RejectsHighNoSpeechProbability()
    {
        var result = _sut.Process("coś tam", noSpeechProbability: 0.9f);
        Assert.Null(result);
    }

    [Fact]
    public void Process_RejectsLowConfidence()
    {
        var result = _sut.Process("coś tam", avgProbability: 0.05f);
        Assert.Null(result);
    }

    [Fact]
    public void Process_AcceptsConfidentSpeech()
    {
        var result = _sut.Process("dzień dobry", noSpeechProbability: 0.1f, avgProbability: 0.9f);
        Assert.Equal("Dzień dobry.", result);
    }

    [Fact]
    public void Process_RealSpeechContainingArtifactWord_IsNotRejected()
    {
        // „dziękuję" samo w sobie nie jest halucynacją — tylko pełna stopka „dziękuję za uwagę".
        var result = _sut.Process("dziękuję bardzo za pomoc");
        Assert.Equal("Dziękuję bardzo za pomoc.", result);
    }
}
