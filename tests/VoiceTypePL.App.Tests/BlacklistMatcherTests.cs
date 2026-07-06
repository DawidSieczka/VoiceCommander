using VoiceTypePL.App.Settings;

namespace VoiceTypePL.App.Tests;

public class BlacklistMatcherTests
{
    [Theory]
    [InlineData("witcher3", "witcher3")]
    [InlineData("Witcher3", "witcher3")]
    [InlineData("witcher3", "Witcher3.EXE")]
    [InlineData("witcher3", "  witcher3.exe  ")]
    public void IsBlacklisted_MatchesNormalizedEntries(string processName, string entry)
    {
        Assert.True(BlacklistMatcher.IsBlacklisted(processName, new[] { "notepad", entry }));
    }

    [Theory]
    [InlineData("witcher3", "witcher")]      // prefiks to nie dopasowanie
    [InlineData("notepad", "notepad++")]
    [InlineData("", "cokolwiek")]
    [InlineData(null, "cokolwiek")]
    public void IsBlacklisted_RejectsNonMatches(string? processName, string entry)
    {
        Assert.False(BlacklistMatcher.IsBlacklisted(processName, new[] { entry }));
    }

    [Fact]
    public void IsBlacklisted_EmptyListNeverMatches()
    {
        Assert.False(BlacklistMatcher.IsBlacklisted("witcher3", Array.Empty<string>()));
    }
}
