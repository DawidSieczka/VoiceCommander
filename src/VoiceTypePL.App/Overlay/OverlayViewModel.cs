using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoiceTypePL.App.Overlay;

/// <summary>
/// Model widoku dymka: tekst zdania (edytowalny) i licznik oczekujących zdań w kolejce.
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private int _pendingCount;

    /// <summary>Tekst bieżącego zdania (dwukierunkowo z edytowalnym polem w dymku).</summary>
    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    /// <summary>Liczba zdań czekających w kolejce za bieżącym.</summary>
    public int PendingCount
    {
        get => _pendingCount;
        set
        {
            if (Set(ref _pendingCount, value))
            {
                Raise(nameof(HasPending));
                Raise(nameof(PendingText));
            }
        }
    }

    /// <summary>Czy jest cokolwiek w kolejce (do sterowania widocznością licznika).</summary>
    public bool HasPending => _pendingCount > 0;

    /// <summary>Etykieta „+N oczekujące" (§5.4).</summary>
    public string PendingText => _pendingCount > 0 ? $"+{_pendingCount} oczekujące" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
