using VoiceTypePL.Core.Injection;

namespace VoiceTypePL.Injection;

/// <summary>Parametry wstrzykiwania (§5.5). Z configu; pełne UI w Etapie 7.</summary>
public sealed class InjectionOptions
{
    /// <summary>Strategia wpisywania: schowek (domyślnie) albo Unicode SendInput.</summary>
    public InjectionStrategy Strategy { get; set; } = InjectionStrategy.Clipboard;

    /// <summary>
    /// Czy kliknąć w pozycji kursora, by ustawić fokus/karetkę, gdy brak aktywnej karetki
    /// (heurystyka <c>GUITHREADINFO.hwndCaret</c>). Gdy karetka jest — klik jest pomijany.
    /// </summary>
    public bool ClickToFocus { get; set; } = true;

    /// <summary>Po ilu ms przywrócić poprzednią zawartość schowka (czas na odczyt Ctrl+V przez cel).</summary>
    public int ClipboardRestoreDelayMs { get; set; } = 150;

    /// <summary>Czy dopiąć spację separującą po wpisanym zdaniu (§5.5.4).</summary>
    public bool AppendSpace { get; set; } = true;

    /// <summary>Czy pomijać pola hasła (wykrycie przez UIA <c>IsPassword</c>, §5.5).</summary>
    public bool SkipPasswordFields { get; set; } = true;
}
