using System.Globalization;
using System.Text;

namespace VoiceTypePL.Stt;

/// <summary>
/// Porządkuje surowy tekst z Whispera (§5.3): przycięcie i scalenie białych znaków, odrzucenie
/// halucynacji (znane artefakty + progi no_speech / pewności), kapitalizacja pierwszej litery
/// oraz dopięcie kropki, gdy brak interpunkcji końcowej. Klasa jest czysta (bez zależności od
/// Whispera/IO), więc w pełni testowalna bez modelu.
/// </summary>
public sealed class TranscriptPostProcessor
{
    // Znaki uznawane za poprawne zakończenie zdania — jeśli tekst kończy się innym, dopinamy kropkę.
    private static readonly char[] TerminalPunctuation = { '.', '!', '?', '…', ':', ';' };

    private readonly TranscriptPostProcessorOptions _options;
    private readonly string[] _normalizedPhrases;

    public TranscriptPostProcessor(TranscriptPostProcessorOptions? options = null)
    {
        _options = options ?? new TranscriptPostProcessorOptions();
        _normalizedPhrases = _options.HallucinationPhrases
            .Select(NormalizeForMatch)
            .Where(p => p.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Zwraca uporządkowany tekst zdania albo <c>null</c>, gdy segment należy odrzucić
    /// (pusty, halucynacja lub zbyt niska pewność).
    /// </summary>
    /// <param name="rawText">Surowy tekst z Whispera (możliwe wiodące spacje, brak interpunkcji).</param>
    /// <param name="noSpeechProbability">no_speech_prob z Whispera (0–1); &gt; próg = odrzuć.</param>
    /// <param name="avgProbability">Średnia pewność tokenów (0–1); &lt; próg = odrzuć. 1 = brak danych.</param>
    public string? Process(string? rawText, float noSpeechProbability = 0f, float avgProbability = 1f)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var text = CollapseWhitespace(rawText);
        if (text.Length == 0)
        {
            return null;
        }

        if (noSpeechProbability > _options.MaxNoSpeechProbability)
        {
            return null;
        }

        if (_options.MinAvgProbability > 0f && avgProbability < _options.MinAvgProbability)
        {
            return null;
        }

        if (IsHallucination(text))
        {
            return null;
        }

        text = Capitalize(text);
        text = EnsureTerminalPunctuation(text);
        return text;
    }

    /// <summary>Czy tekst to znany artefakt/halucynacja (dopasowanie bez wielkości liter i diakrytyków).</summary>
    public bool IsHallucination(string text)
    {
        var normalized = NormalizeForMatch(text);
        if (normalized.Length == 0)
        {
            return true;
        }

        foreach (var phrase in _normalizedPhrases)
        {
            if (normalized.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                builder.Append(ch);
                previousWasSpace = false;
            }
        }

        return builder.ToString();
    }

    private static string Capitalize(string text)
    {
        if (text.Length == 0 || char.IsUpper(text[0]))
        {
            return text;
        }

        return char.ToUpper(text[0], CultureInfo.GetCultureInfo("pl-PL")) + text[1..];
    }

    private static string EnsureTerminalPunctuation(string text)
    {
        var last = text[^1];
        return Array.IndexOf(TerminalPunctuation, last) >= 0 ? text : text + '.';
    }

    /// <summary>Do porównań: małe litery, bez diakrytyków, przycięte, ze scalonymi spacjami.</summary>
    private static string NormalizeForMatch(string text)
    {
        var collapsed = CollapseWhitespace(text).ToLower(CultureInfo.GetCultureInfo("pl-PL"));
        // 'ł' nie rozkłada się przez FormD (to osobna litera, nie baza + znak diakrytyczny) — mapujemy ręcznie.
        collapsed = collapsed.Replace('ł', 'l');
        var decomposed = collapsed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
