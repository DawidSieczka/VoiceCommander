using System.Globalization;

namespace VoiceTypePL.Core.History;

/// <summary>
/// Historia wpisanych zdań (§5.7). Role: (a) walidacja przy edycji — porównanie tekstu zaznaczonego
/// przez EditEngine z historią (fuzzy match ≥ 85%) w Etapie 5/6; (b) panel „ostatnie zdania" w
/// ustawieniach (Etap 7). Trzyma ograniczoną liczbę najnowszych wpisów; wątkowo bezpieczny.
/// </summary>
public sealed class SentenceRegistry
{
    private readonly object _gate = new();
    private readonly LinkedList<RegisteredSentence> _items = new();
    private readonly int _capacity;

    public SentenceRegistry(int capacity = 200)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Dopisuje zdanie na początek historii (i przycina do pojemności).</summary>
    public void Record(RegisteredSentence sentence)
    {
        lock (_gate)
        {
            _items.AddFirst(sentence);
            while (_items.Count > _capacity)
            {
                _items.RemoveLast();
            }
        }
    }

    /// <summary>Najnowsze wpisy (od najświeższego).</summary>
    public IReadOnlyList<RegisteredSentence> Recent(int count = 20)
    {
        lock (_gate)
        {
            return _items.Take(Math.Max(0, count)).ToList();
        }
    }

    public int Count
    {
        get { lock (_gate) { return _items.Count; } }
    }

    /// <summary>
    /// Najlepiej pasujący wpis dla podanego tekstu (§5.7: fuzzy match). Zwraca <c>null</c>, gdy żaden
    /// nie osiąga <paramref name="minSimilarity"/>. Porównanie bez rozróżniania wielkości liter i
    /// nadmiarowych spacji.
    /// </summary>
    public RegisteredSentence? FindBestMatch(string text, double minSimilarity = 0.85)
    {
        var needle = Normalize(text);
        if (needle.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            RegisteredSentence? best = null;
            var bestScore = minSimilarity;
            foreach (var item in _items)
            {
                var score = Similarity(needle, Normalize(item.Text));
                if (score >= bestScore)
                {
                    bestScore = score;
                    best = item;
                }
            }

            return best;
        }
    }

    /// <summary>Znormalizowane podobieństwo dwóch napisów w [0, 1] (1 = identyczne) na bazie Levenshteina.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1.0;
        }

        var distance = Levenshtein(a, b);
        var max = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / max;
    }

    private static string Normalize(string text)
    {
        var lowered = text.Trim().ToLower(CultureInfo.GetCultureInfo("pl-PL"));
        return string.Join(' ', lowered.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
