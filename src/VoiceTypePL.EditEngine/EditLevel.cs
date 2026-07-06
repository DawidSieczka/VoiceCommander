namespace VoiceTypePL.EditEngine;

/// <summary>
/// Poziom realizacji edycji zdania (§5.6), próbowany kolejno i cache'owany per aplikacja.
/// </summary>
public enum EditLevel
{
    /// <summary>Poziom 1 — UI Automation (pełna precyzja): <see cref="UiaSentenceLocator"/>.</summary>
    Uia,

    /// <summary>Poziom 2 — heurystyka kliknięć (dwuklik + rozszerzanie), fallback do całej linii.</summary>
    Click,

    /// <summary>Poziom 3 — tryb ręczny: użytkownik sam zaznaczył, my tylko podmieniamy.</summary>
    Manual,
}
