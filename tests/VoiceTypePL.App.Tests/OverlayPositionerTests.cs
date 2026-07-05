using VoiceTypePL.App.Overlay;

namespace VoiceTypePL.App.Tests;

/// <summary>
/// Deterministyczne testy pozycjonowania dymka (§5.4) — obliczenia w fizycznych pikselach, skalowanie
/// DPI i clamping do obszaru roboczego, bez WPF/Win32. To rdzeń weryfikacji kryterium „różne DPI".
/// </summary>
public sealed class OverlayPositionerTests
{
    // Monitor główny 1920×1080 (work area do 1040, pasek zadań 40 px), 96 DPI (100%).
    private static MonitorGeometry Primary96 => new(new PixelRect(0, 0, 1920, 1040), 96);

    // Monitor po prawej, 200% (192 DPI), origin fizyczny 1920,0, 2560×1440 → work area 1400.
    private static MonitorGeometry Right192 => new(new PixelRect(1920, 0, 2560, 1400), 192);

    [Fact]
    public void Place_ScalesWindowSizeByDpi()
    {
        var at96 = OverlayPositioner.Place(400, 400, 380, 150, Primary96);
        Assert.Equal(380, at96.Width);
        Assert.Equal(150, at96.Height);

        var at192 = OverlayPositioner.Place(2200, 300, 380, 150, Right192);
        Assert.Equal(760, at192.Width);      // 380 × 2.0
        Assert.Equal(300, at192.Height);     // 150 × 2.0
    }

    [Fact]
    public void Place_PutsBubbleBottomRightOfCursor_WhenRoom()
    {
        var rect = OverlayPositioner.Place(400, 400, 380, 150, Primary96);
        var offset = (int)Math.Round(OverlayPositioner.CursorOffsetDip); // 18 px @96

        Assert.Equal(400 + offset, rect.X);
        Assert.Equal(400 + offset, rect.Y);
    }

    [Fact]
    public void Place_FlipsToLeft_WhenNearRightEdge()
    {
        // Kursor tuż przy prawej krawędzi — okno nie zmieści się w prawo, ma pójść w lewo od kursora.
        var rect = OverlayPositioner.Place(1900, 400, 380, 150, Primary96);
        Assert.True(rect.Right <= Primary96.WorkArea.Right, "Dymek nie może wystawać poza prawą krawędź.");
        Assert.True(rect.X < 1900, "Przy prawej krawędzi dymek ma być na lewo od kursora.");
    }

    [Fact]
    public void Place_FlipsUp_WhenNearBottomEdge()
    {
        var rect = OverlayPositioner.Place(400, 1030, 380, 150, Primary96);
        Assert.True(rect.Bottom <= Primary96.WorkArea.Bottom, "Dymek nie może wystawać poza dolną krawędź.");
        Assert.True(rect.Y < 1030, "Przy dolnej krawędzi dymek ma być powyżej kursora.");
    }

    [Fact]
    public void Place_ClampsWithinWorkArea_OnAllSides()
    {
        // Skrajny róg — po sprawdzeniu i tak w całości w obszarze roboczym.
        var rect = OverlayPositioner.Place(1919, 1039, 380, 150, Primary96);
        Assert.True(rect.X >= Primary96.WorkArea.X);
        Assert.True(rect.Y >= Primary96.WorkArea.Y);
        Assert.True(rect.Right <= Primary96.WorkArea.Right);
        Assert.True(rect.Bottom <= Primary96.WorkArea.Bottom);
    }

    [Fact]
    public void Place_StaysOnSecondaryMonitor_WithItsOwnDpiAndOrigin()
    {
        // Kursor na prawym monitorze (200%). Dymek ma zostać na tym monitorze (X ≥ 1920) i się nie
        // wysunąć poza jego obszar roboczy.
        var rect = OverlayPositioner.Place(2200, 300, 380, 150, Right192);
        Assert.True(rect.X >= Right192.WorkArea.X, "Dymek ma zostać na monitorze pod kursorem.");
        Assert.True(rect.Right <= Right192.WorkArea.Right);
        Assert.True(rect.Bottom <= Right192.WorkArea.Bottom);
    }
}
