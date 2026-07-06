namespace VoiceTypePL.App.Tests;

public class AppStateTests
{
    [Fact]
    public void EffectivePause_CombinesManualAndAuto()
    {
        var state = new AppState();
        Assert.False(state.IsEffectivelyPaused);

        state.IsPaused = true;
        Assert.True(state.IsEffectivelyPaused);

        state.IsAutoPaused = true;
        state.IsPaused = false;
        Assert.True(state.IsEffectivelyPaused);      // auto-pauza trzyma nasłuch wstrzymany

        state.IsAutoPaused = false;
        Assert.False(state.IsEffectivelyPaused);
    }

    [Fact]
    public void PausedChanged_RaisedOnlyWhenEffectiveStateChanges()
    {
        var state = new AppState();
        var events = new List<bool>();
        state.PausedChanged += (_, paused) => events.Add(paused);

        state.IsPaused = true;          // false → true: zdarzenie
        state.IsAutoPaused = true;      // efektywnie bez zmiany: brak zdarzenia
        state.IsPaused = false;         // wciąż auto-pauza: brak zdarzenia
        state.IsAutoPaused = false;     // true → false: zdarzenie

        Assert.Equal(new[] { true, false }, events);
    }
}
