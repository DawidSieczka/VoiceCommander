using VoiceTypePL.EditEngine;

namespace VoiceTypePL.EditEngine.Tests;

/// <summary>Testy cache poziomu edycji per proces (§5.6, Etap 6) — czysta logika.</summary>
public sealed class EditLevelCacheTests
{
    [Fact]
    public void Unknown_ReturnsNull()
    {
        var cache = new EditLevelCache();
        Assert.Null(cache.Get("notepad"));
    }

    [Fact]
    public void Set_ThenGet_ReturnsLevel()
    {
        var cache = new EditLevelCache();
        cache.Set("notepad", EditLevel.Uia);
        Assert.Equal(EditLevel.Uia, cache.Get("notepad"));
    }

    [Fact]
    public void Set_Overwrites()
    {
        var cache = new EditLevelCache();
        cache.Set("acme", EditLevel.Uia);
        cache.Set("acme", EditLevel.Click);
        Assert.Equal(EditLevel.Click, cache.Get("acme"));
    }

    [Fact]
    public void ProcessName_IsCaseInsensitive()
    {
        var cache = new EditLevelCache();
        cache.Set("Notepad", EditLevel.Click);
        Assert.Equal(EditLevel.Click, cache.Get("notepad"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankProcessName_IsIgnored(string? name)
    {
        var cache = new EditLevelCache();
        cache.Set(name, EditLevel.Uia);
        Assert.Null(cache.Get(name));
    }
}
