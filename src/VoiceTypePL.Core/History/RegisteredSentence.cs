using VoiceTypePL.Core.Injection;

namespace VoiceTypePL.Core.History;

/// <summary>
/// Wpis w historii wpisanych zdań (§5.7): treść, kiedy i dokąd trafiła, oraz jaką strategią.
/// Rejestr celowo NIE śledzi pozycji tekstu po wpisaniu (scroll/edycja ją unieważniają) — pozycję
/// przy edycji wyznacza kursor myszy (§5.7, świadome ograniczenie).
/// </summary>
public sealed class RegisteredSentence
{
    public RegisteredSentence(
        string text,
        DateTimeOffset timestamp,
        string? windowTitle,
        string? processName,
        int processId,
        InjectionStrategy strategy)
    {
        Text = text;
        Timestamp = timestamp;
        WindowTitle = windowTitle;
        ProcessName = processName;
        ProcessId = processId;
        Strategy = strategy;
    }

    public string Text { get; }
    public DateTimeOffset Timestamp { get; }
    public string? WindowTitle { get; }
    public string? ProcessName { get; }
    public int ProcessId { get; }
    public InjectionStrategy Strategy { get; }
}
