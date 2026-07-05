namespace VoiceTypePL.Injection;

/// <summary>Jedno naciśnięcie/zwolnienie klawisza „Unicode" (kod jednostki UTF-16, kierunek).</summary>
public readonly record struct UnicodeKeyStroke(ushort CodeUnit, bool KeyUp);

/// <summary>
/// Zamienia napis na sekwencję zdarzeń <c>KEYEVENTF_UNICODE</c> (§5.5, strategia „Unicode SendInput").
/// Czysta logika — bez Win32 — więc testowalna. Iteruje po jednostkach UTF-16: pary zastępcze (emoji
/// itp.) rozpadają się na dwa kody, które Windows składa z powrotem, gdy wysłać je po kolei.
/// </summary>
public static class UnicodeInputBuilder
{
    /// <summary>Dla każdej jednostki UTF-16 zwraca parę zdarzeń: wciśnięcie i zwolnienie.</summary>
    public static IReadOnlyList<UnicodeKeyStroke> Build(string text)
    {
        var strokes = new List<UnicodeKeyStroke>(text.Length * 2);
        foreach (var ch in text)
        {
            var code = (ushort)ch;
            strokes.Add(new UnicodeKeyStroke(code, KeyUp: false));
            strokes.Add(new UnicodeKeyStroke(code, KeyUp: true));
        }

        return strokes;
    }
}
