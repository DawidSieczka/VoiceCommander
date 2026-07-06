namespace VoiceTypePL.Core.Injection;

/// <summary>
/// Sterowanie zaznaczeniem w cudzym oknie przez syntetyczne wejście (§5.6, Poziomy 2/3). Abstrakcja z
/// §2 — implementacja Win32 (SendInput + schowek) siedzi w warstwie <c>VoiceTypePL.Injection</c>, a
/// EditEngine zna tylko interfejs (dzięki temu logikę Poziomu 2 da się testować na atrapie). Poziom 2
/// celowo używa pojedynczego kliknięcia + klawiatury (Home/End/strzałki), zamiast polegać na tym, że
/// system rozpozna syntetyczny dwuklik/trzyklik — to znacznie pewniejsze. Wszystkie metody wołane z
/// wątku UI (STA — wymóg schowka WPF).
/// </summary>
public interface ISelectionController
{
    /// <summary>
    /// Odczytuje bieżące zaznaczenie z okna pierwszoplanowego bez jego naruszania. Technika sentinela:
    /// zapisz schowek → wstaw unikalny znacznik → Ctrl+C → odczytaj; gdy wynik == znacznik, nic nie było
    /// zaznaczone (zwraca <c>null</c>); na końcu przywróć oryginalny schowek. Zasila detekcję Poziomu 3
    /// oraz odczyt linii/offsetu w Poziomie 2.
    /// </summary>
    Task<string?> ReadSelectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Pojedynczy klik LPM w punkcie ekranu (px) — ustawia fokus i karetkę w miejscu kliknięcia.</summary>
    void Click(int screenX, int screenY);

    /// <summary>Karetka na początek linii (klawisz <c>Home</c>).</summary>
    void MoveToLineStart();

    /// <summary>Rozszerza zaznaczenie do początku linii (<c>Shift+Home</c>) — do zmierzenia offsetu karetki.</summary>
    void ExtendToLineStart();

    /// <summary>Rozszerza zaznaczenie do końca linii (<c>Shift+End</c>) — do odczytu całej linii.</summary>
    void ExtendToLineEnd();

    /// <summary>Przesuwa karetkę w prawo o <paramref name="steps"/> znaków (klawisz <c>Right</c>), bez zaznaczania.</summary>
    void MoveCaretRight(int steps);

    /// <summary>Rozszerza zaznaczenie w prawo o <paramref name="steps"/> znaków (<c>Shift+Right</c>).</summary>
    void ExtendRight(int steps);
}
