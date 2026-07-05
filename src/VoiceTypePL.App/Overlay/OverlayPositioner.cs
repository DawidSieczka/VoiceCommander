namespace VoiceTypePL.App.Overlay;

/// <summary>Prostokąt w fizycznych pikselach ekranu (nie DIP-y WPF).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>Geometria monitora: obszar roboczy (bez paska zadań) w px + DPI monitora.</summary>
public readonly record struct MonitorGeometry(PixelRect WorkArea, double Dpi)
{
    /// <summary>Skala względem 96 DPI (100%).</summary>
    public double Scale => Dpi / 96.0;
}

/// <summary>
/// Wyznacza pozycję dymka względem kursora, w fizycznych pikselach docelowego monitora (§5.4).
/// Czyste, deterministyczne obliczenia (bez WPF/Win32), więc w pełni testowalne — reszta (odczyt
/// kursora, DPI, praca na HWND) jest cienką warstwą wokół tej klasy. Dymek pozycjonujemy przez
/// <c>SetWindowPos</c> w px, a nie przez WPF <c>Left/Top</c> (DIP-y), bo te ostatnie źle skalują się
/// między monitorami o różnym DPI.
/// </summary>
public static class OverlayPositioner
{
    /// <summary>Odsunięcie dymka od kursora w DIP-ach (skalowane DPI monitora).</summary>
    public const double CursorOffsetDip = 18;

    /// <summary>
    /// Zwraca prostokąt dymka (px) dla kursora w <paramref name="cursorX"/>/<paramref name="cursorY"/>
    /// (px) i rozmiaru okna w DIP-ach. Domyślnie w prawo-dół od kursora; przy krawędzi „odbija się"
    /// na drugą stronę, a na końcu jest przycięty do obszaru roboczego monitora.
    /// </summary>
    public static PixelRect Place(int cursorX, int cursorY, double widthDip, double heightDip, MonitorGeometry monitor)
    {
        var scale = monitor.Scale;
        var width = (int)Math.Round(widthDip * scale);
        var height = (int)Math.Round(heightDip * scale);
        var offset = (int)Math.Round(CursorOffsetDip * scale);
        var workArea = monitor.WorkArea;

        // Preferencja: prawy-dolny róg względem kursora.
        var x = cursorX + offset;
        var y = cursorY + offset;

        // Jeśli nie mieści się po tej stronie — połóż po przeciwnej stronie kursora.
        if (x + width > workArea.Right)
        {
            x = cursorX - offset - width;
        }

        if (y + height > workArea.Bottom)
        {
            y = cursorY - offset - height;
        }

        // Ostateczne przycięcie do obszaru roboczego (gdy okno szersze/wyższe niż zostało miejsca).
        x = Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - width));
        y = Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));

        return new PixelRect(x, y, width, height);
    }

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;
}
