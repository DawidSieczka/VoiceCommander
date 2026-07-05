using VoiceTypePL.Core.Speech;

namespace VoiceTypePL.App.Overlay;

/// <summary>
/// Kolejka zdań czekających na zatwierdzenie w dymku (§5.4: „jeśli użytkownik dyktuje szybciej niż
/// zatwierdza, zdania czekają w kolejce"). Jednocześnie pokazywane jest jedno zdanie (<see cref="Current"/>),
/// reszta czeka; <see cref="PendingCount"/> zasila licznik „+N oczekujące". Klasa nie jest wątkowo
/// bezpieczna — jest używana wyłącznie z wątku UI (tak jak reszta OverlayService).
/// </summary>
public sealed class OverlaySentenceQueue
{
    private readonly Queue<TranscribedSentence> _pending = new();

    /// <summary>Zdanie aktualnie pokazane w dymku (albo <c>null</c>, gdy dymek pusty).</summary>
    public TranscribedSentence? Current { get; private set; }

    /// <summary>Liczba zdań czekających za bieżącym.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Dokłada zdanie. Zwraca <c>true</c>, gdy stało się bieżącym (dymek trzeba pokazać teraz);
    /// <c>false</c>, gdy trafiło do kolejki (dymek już widoczny — zaktualizuj tylko licznik).
    /// </summary>
    public bool Enqueue(TranscribedSentence sentence)
    {
        if (Current is null)
        {
            Current = sentence;
            return true;
        }

        _pending.Enqueue(sentence);
        return false;
    }

    /// <summary>
    /// Przechodzi do następnego zdania (po zatwierdzeniu/odrzuceniu bieżącego). Zwraca nowe bieżące
    /// albo <c>null</c>, gdy kolejka pusta (dymek należy ukryć).
    /// </summary>
    public TranscribedSentence? Advance()
    {
        Current = _pending.Count > 0 ? _pending.Dequeue() : null;
        return Current;
    }

    /// <summary>Czyści całą kolejkę i bieżące zdanie.</summary>
    public void Clear()
    {
        _pending.Clear();
        Current = null;
    }
}
