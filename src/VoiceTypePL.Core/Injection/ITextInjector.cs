namespace VoiceTypePL.Core.Injection;

/// <summary>Sposób „wpisania" tekstu do aktywnej kontrolki (§5.5).</summary>
public enum InjectionStrategy
{
    /// <summary>Schowek + Ctrl+V (domyślny) — szybki, odporny na polskie znaki i układ klawiatury.</summary>
    Clipboard,

    /// <summary>Unicode SendInput znak po znaku — wolniejszy, działa tam, gdzie wklejanie jest blokowane.</summary>
    UnicodeSendInput,
}

/// <summary>Wynik próby wstrzyknięcia tekstu — zasila log i <see cref="SentenceRegistry"/> (§5.7).</summary>
public sealed record InjectionResult(
    bool Success,
    InjectionStrategy Strategy,
    string? WindowTitle,
    string? ProcessName,
    int ProcessId,
    string? SkippedReason = null)
{
    public static InjectionResult Skipped(string reason, InjectionStrategy strategy) =>
        new(false, strategy, null, null, 0, reason);
}

/// <summary>
/// „Wpisuje" tekst w miejscu kursora myszy (§5.5). Abstrakcja z §2 — implementacja Win32 (klik +
/// schowek/SendInput) siedzi w warstwie <c>VoiceTypePL.Injection</c>, a reszta zna tylko interfejs.
/// </summary>
public interface ITextInjector
{
    /// <summary>
    /// Wstrzykuje <paramref name="text"/> do kontrolki pod kursorem. Zwraca wynik (sukces + dane okna
    /// docelowego, albo powód pominięcia — np. pole hasła). Wołane z wątku UI (STA — wymóg schowka).
    /// </summary>
    /// <param name="appendSpace">
    /// Nadpisuje ustawienie dopinania spacji: <c>null</c> = użyj domyślnego z opcji; <c>false</c> np.
    /// przy edycji (nadpisujemy zaznaczenie, bez spacji na końcu).
    /// </param>
    /// <param name="clickToFocus">
    /// Nadpisuje klik ustawiający fokus: <c>null</c> = domyślne z opcji; <c>false</c> przy edycji —
    /// mamy już zaznaczenie do nadpisania, a klik zwinąłby je i wstawił tekst w pozycji myszy.
    /// </param>
    Task<InjectionResult> InjectAsync(
        string text,
        bool? appendSpace = null,
        bool? clickToFocus = null,
        CancellationToken cancellationToken = default);
}
