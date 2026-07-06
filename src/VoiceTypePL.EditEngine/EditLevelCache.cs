namespace VoiceTypePL.EditEngine;

/// <summary>
/// Cache poziomu edycji per proces docelowy (§5.6: „wynik detekcji poziomu jest cache'owany per proces
/// docelowy, np. notepad.exe → Poziom 1"). Trzymany tylko w pamięci sesji — aplikacje bywają
/// aktualizowane i zmieniają wsparcie UIA, więc świeży start po restarcie jest bezpieczniejszy.
/// Wątkowo bezpieczny (jak <c>SentenceRegistry</c>). Czysta logika — testowalna jednostkowo.
/// </summary>
public sealed class EditLevelCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EditLevel> _byProcess = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zapamiętany poziom dla procesu (np. „notepad") albo <c>null</c>, gdy nieznany.</summary>
    public EditLevel? Get(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        lock (_gate)
        {
            return _byProcess.TryGetValue(processName, out var level) ? level : null;
        }
    }

    /// <summary>Zapisuje/nadpisuje poziom dla procesu. Puste nazwy są ignorowane.</summary>
    public void Set(string? processName, EditLevel level)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        lock (_gate)
        {
            _byProcess[processName] = level;
        }
    }
}
