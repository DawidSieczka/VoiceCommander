namespace VoiceTypePL.App;

/// <summary>
/// Współdzielony stan runtime aplikacji.
/// W Etapie 0 zawiera wyłącznie przełącznik pauzy — zwykły bool w pamięci, bez wpływu
/// na cokolwiek (pipeline audio/VAD/STT jeszcze nie istnieje). W Etapie 7 pauza będzie
/// realnie wstrzymywać przetwarzanie.
/// </summary>
public sealed class AppState
{
    private bool _isPaused;

    /// <summary>Czy nasłuch jest wstrzymany (szkielet — na razie nic nie wstrzymuje).</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused == value)
            {
                return;
            }

            _isPaused = value;
            PausedChanged?.Invoke(this, value);
        }
    }

    /// <summary>Zgłaszane po zmianie stanu pauzy (np. do aktualizacji ikony/menu zasobnika).</summary>
    public event EventHandler<bool>? PausedChanged;
}
