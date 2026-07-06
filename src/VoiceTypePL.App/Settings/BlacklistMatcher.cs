namespace VoiceTypePL.App.Settings;

/// <summary>
/// Czysta logika czarnej listy (§5.8): dopasowanie nazwy procesu do wpisów użytkownika.
/// Wpisy są normalizowane — bez rozróżniania wielkości liter, z obcięciem <c>.exe</c> i spacji —
/// żeby „Witcher3.EXE" na liście łapało proces „witcher3".
/// </summary>
public static class BlacklistMatcher
{
    /// <summary>Czy proces o podanej nazwie (bez ścieżki) jest na czarnej liście.</summary>
    public static bool IsBlacklisted(string? processName, IEnumerable<string> blacklist)
    {
        var normalized = Normalize(processName);
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var entry in blacklist)
        {
            if (Normalize(entry) == normalized)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Normalizuje wpis/nazwę procesu: trim, małe litery, bez rozszerzenia <c>.exe</c>.</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim().ToLowerInvariant();
        return trimmed.EndsWith(".exe", StringComparison.Ordinal) ? trimmed[..^4] : trimmed;
    }
}
