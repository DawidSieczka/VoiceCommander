namespace VoiceTypePL.EditEngine;

/// <summary>
/// Wyznacza granice zdania wokół punktu w tekście (§5.6, Poziom 1 — „ekspansja ręczna: rozszerzanie
/// po znaku w obu kierunkach do <c>. ! ? \n</c> z limitem 500 znaków"). Czysta logika (bez UIA),
/// więc testowalna; używana zawsze — także gdy UIA zwróci akapit/linię — żeby zawęzić do jednego zdania.
/// </summary>
public static class SentenceBoundary
{
    /// <summary>Znaki kończące zdanie (kropka/wykrzyknik/pytajnik) — wliczane do zdania.</summary>
    private static bool IsSentencePunctuation(char c) => c is '.' or '!' or '?' or '…';

    /// <summary>Znaki kończące zakres (interpunkcja + nowa linia). Nowa linia NIE jest wliczana.</summary>
    private static bool IsBoundary(char c) => IsSentencePunctuation(c) || c is '\n' or '\r';

    /// <summary>
    /// Zwraca (początek, długość) zdania zawierającego pozycję <paramref name="index"/> w
    /// <paramref name="text"/>. Wynik jest przycięty do <paramref name="maxChars"/> wokół punktu.
    /// </summary>
    public static (int Start, int Length) Expand(string text, int index, int maxChars = 500)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        var n = text.Length;
        index = Math.Clamp(index, 0, n - 1);

        // W lewo: aż za poprzedni znak graniczny (początek zdania jest tuż po nim).
        var start = index;
        while (start > 0 && !IsBoundary(text[start - 1]))
        {
            start--;
        }

        // Pomiń wiodące białe znaki zdania — ale nie przeskocz punktu kliknięcia.
        while (start < index && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        // W prawo: do pierwszego znaku granicznego; interpunkcję zdaniową wliczamy, nowej linii nie.
        var end = index;
        while (end < n && !IsBoundary(text[end]))
        {
            end++;
        }

        if (end < n && IsSentencePunctuation(text[end]))
        {
            end++;
        }

        var length = end - start;
        if (length > maxChars)
        {
            // Za długie (np. brak interpunkcji) — przytnij symetrycznie wokół punktu.
            start = Math.Max(start, index - maxChars / 2);
            end = Math.Min(end, start + maxChars);
            length = end - start;
        }

        return (start, Math.Max(0, length));
    }
}
