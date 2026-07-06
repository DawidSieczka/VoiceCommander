namespace VoiceTypePL.App;

/// <summary>
/// Współdzielony stan runtime aplikacji: pauza ręczna (menu zasobnika), auto-pauza z czarnej listy
/// (§5.8 — cofa się sama po opuszczeniu aplikacji z listy) oraz bieżący poziom sygnału mikrofonu
/// (RMS ostatniej ramki — wskaźnik w oknie ustawień, §5.1).
/// </summary>
public sealed class AppState
{
    private bool _isPaused;
    private bool _isAutoPaused;
    private float _signalLevel;

    /// <summary>Pauza ręczna — przełącznik w menu zasobnika.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set => SetPause(ref _isPaused, value);
    }

    /// <summary>Auto-pauza: okno pierwszoplanowe należy do procesu z czarnej listy.</summary>
    public bool IsAutoPaused
    {
        get => _isAutoPaused;
        set => SetPause(ref _isAutoPaused, value);
    }

    /// <summary>Czy nasłuch jest faktycznie wstrzymany (ręcznie lub przez czarną listę).</summary>
    public bool IsEffectivelyPaused => _isPaused || _isAutoPaused;

    /// <summary>
    /// Poziom sygnału mikrofonu (RMS ostatniej ramki, 0–1). Pisany z wątku audio, czytany przez UI
    /// (odpytywanie timerem) — pojedynczy float, bez potrzeby synchronizacji.
    /// </summary>
    public float SignalLevel
    {
        get => _signalLevel;
        set => _signalLevel = value;
    }

    /// <summary>Zgłaszane, gdy zmienia się FAKTYCZNY stan pauzy (<see cref="IsEffectivelyPaused"/>).</summary>
    public event EventHandler<bool>? PausedChanged;

    private void SetPause(ref bool field, bool value)
    {
        if (field == value)
        {
            return;
        }

        var before = IsEffectivelyPaused;
        field = value;
        var after = IsEffectivelyPaused;
        if (before != after)
        {
            PausedChanged?.Invoke(this, after);
        }
    }
}
