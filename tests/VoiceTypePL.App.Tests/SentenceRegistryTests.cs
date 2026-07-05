using VoiceTypePL.Core.History;
using VoiceTypePL.Core.Injection;

namespace VoiceTypePL.App.Tests;

/// <summary>Testy rejestru zdań (§5.7): historia, pojemność i dopasowanie rozmyte (pod Etap 5).</summary>
public sealed class SentenceRegistryTests
{
    private static RegisteredSentence Make(string text) =>
        new(text, DateTimeOffset.Now, "Okno", "proc", 123, InjectionStrategy.Clipboard);

    [Fact]
    public void Recent_ReturnsNewestFirst()
    {
        var registry = new SentenceRegistry();
        registry.Record(Make("pierwsze"));
        registry.Record(Make("drugie"));

        var recent = registry.Recent();

        Assert.Equal("drugie", recent[0].Text);
        Assert.Equal("pierwsze", recent[1].Text);
    }

    [Fact]
    public void Record_RespectsCapacity()
    {
        var registry = new SentenceRegistry(capacity: 2);
        registry.Record(Make("a"));
        registry.Record(Make("b"));
        registry.Record(Make("c"));

        Assert.Equal(2, registry.Count);
        var recent = registry.Recent();
        Assert.Equal("c", recent[0].Text);
        Assert.Equal("b", recent[1].Text);
        Assert.DoesNotContain(recent, s => s.Text == "a");
    }

    [Fact]
    public void FindBestMatch_MatchesDespiteCaseSpacingAndDiacritics()
    {
        var registry = new SentenceRegistry();
        registry.Record(Make("Warszawa jest stolicą Polski."));

        // Inna wielkość liter, brak „ą" i kropki, nadmiarowe spacje — nadal ≥ 85%.
        var match = registry.FindBestMatch("warszawa  jest  stolica polski");

        Assert.NotNull(match);
        Assert.Equal("Warszawa jest stolicą Polski.", match!.Text);
    }

    [Fact]
    public void FindBestMatch_ReturnsNull_ForUnrelatedText()
    {
        var registry = new SentenceRegistry();
        registry.Record(Make("Warszawa jest stolicą Polski."));

        Assert.Null(registry.FindBestMatch("zupełnie inne zdanie o kotach"));
    }

    [Fact]
    public void FindBestMatch_PicksClosestAmongMany()
    {
        var registry = new SentenceRegistry();
        registry.Record(Make("Ala ma kota."));
        registry.Record(Make("Ala ma psa."));
        registry.Record(Make("Zupełnie coś innego."));

        var match = registry.FindBestMatch("ala ma kota");

        Assert.Equal("Ala ma kota.", match!.Text);
    }

    [Theory]
    [InlineData("identyczne", "identyczne", 1.0)]
    [InlineData("", "", 1.0)]
    public void Similarity_Boundaries(string a, string b, double expected)
    {
        Assert.Equal(expected, SentenceRegistry.Similarity(a, b), precision: 5);
    }

    [Fact]
    public void Similarity_DifferentStrings_IsBelowOne()
    {
        Assert.True(SentenceRegistry.Similarity("kot", "pies") < 0.5);
    }
}
