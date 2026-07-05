using VoiceTypePL.Core.Models;

namespace VoiceTypePL.Core.Configuration;

/// <summary>
/// Wczytywanie i zapisywanie konfiguracji aplikacji (plik JSON w %AppData%).
/// Interfejs celowo wydzielony — reszta modułów zależy od abstrakcji, nie od implementacji.
/// </summary>
public interface ISettingsService
{
    /// <summary>Aktualnie wczytana konfiguracja (nigdy null — w razie błędu wartości domyślne).</summary>
    AppSettings Current { get; }

    /// <summary>Pełna ścieżka do pliku konfiguracji.</summary>
    string ConfigFilePath { get; }

    /// <summary>Wczytuje config z dysku (tworzy plik z domyślnymi wartościami, jeśli nie istnieje).</summary>
    AppSettings Load();

    /// <summary>Zapisuje bieżącą konfigurację na dysk.</summary>
    void Save();
}
