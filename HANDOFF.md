# HANDOFF — VoiceType PL (dyktowanie głosowe dla Windows)

Dokument do wznowienia pracy w nowej sesji. Plan całości: **`Dokumentacja-1.md`** (§ w tym pliku
odnoszą się do niej). Ten handoff mówi, co już zrobione, jak to zweryfikowane, jakie są konwencje
i co dalej.

---

## 1. Stan na teraz

Zrobione i działające etapy: **0, 1, 2, 3, 4, 5** (§7 dokumentacji).

| Etap | Zakres | Status |
|---|---|---|
| 0 | Szkielet: Generic Host, DI, Serilog, tray, config JSON | ✅ commit |
| 1 | Audio 16 kHz + Silero VAD → segmenty mowy | ✅ commit |
| 2 | STT Whisper.net (pobieranie modelu, PL, post-processing) | ✅ commit |
| 3 | Dymek overlay (no-activate, DPI, Enter/Esc, kolejka) | ✅ commit |
| 4 | Wstrzykiwanie (klik/schowek/Unicode, przywracanie schowka, SentenceRegistry) | ✅ commit |
| 5 | EditEngine Poziom 1 (UIA: RangeFromPoint → zaznacz zdanie → podmień) | ⚠️ **niezacommitowane** |

**Git:** Etapy 0–4 są w commitach (ostatni `292aafd stage 3 and 4`). **Etap 5 i dwie poprawki
wstrzykiwania czekają w drzewie roboczym** (`git status`) — do zacommitowania ręcznie przez użytkownika.

Build całości: **0 ostrzeżeń / 0 błędów**. Testy: **66/66** (EditEngine 9, VAD 8, App 28, STT 21).

---

## 2. Struktura projektu

```
src/
  VoiceTypePL.Core/        # kontrakty, modele, event-bus (net8.0, bez UI)
    Audio/  Speech/  Injection/  History/  Configuration/  Models/
  VoiceTypePL.Audio/       # WASAPI capture + źródło z pliku WAV (net8.0-windows)
  VoiceTypePL.Vad/         # Silero VAD (ONNX, model osadzony) + segmenter (net8.0)
  VoiceTypePL.Stt/         # Whisper.net: model provider, transcriber, post-processing (net8.0)
  VoiceTypePL.Injection/   # SendInput/schowek/klik, UIA IsPassword (net8.0-windows, UseWPF)
  VoiceTypePL.EditEngine/  # UIA locate+select zdania, SentenceBoundary (net8.0-windows, UseWPF)
  VoiceTypePL.App/         # WPF host: tray, Overlay/, Editing/, pipeline (net8.0-windows)
tests/
  VoiceTypePL.Vad.Tests/  .Stt.Tests/  .App.Tests/  .EditEngine.Tests/
```

**Przepływ end-to-end (dyktowanie):** `IAudioCaptureSource` → `VadSegmenter` (event `SpeechSegmentReady`)
→ `WhisperTranscriber` (kolejka `Channel`, event `SentenceTranscribed`) → `OverlayService` (dymek, kolejka)
→ Enter → `Win32TextInjector` (wpisanie) → `SentenceRegistry`.
**Edycja:** Ctrl+Alt+E → `EditModeService` → `UiaSentenceLocator` (zaznacza zdanie) + `SelectionHighlightWindow`
→ `OverlayService.ReplaceMode=true` → podyktowane zdanie nadpisuje zaznaczenie.

Moduły gadają przez eventy/interfejsy (event-bus z §2) — łatwo podmienić backend (np. STT na Azure przez
`ITranscriber`).

---

## 3. Konwencje (trzymać się ich!)

- **Język polski** w komentarzach, logach, nazwach commitów i handoffach. Kod (identyfikatory) po angielsku.
- **Komentarze** wyjaśniają *dlaczego*, z odwołaniem do sekcji dokumentacji (np. „§5.5 krok 2"). Gęstość
  jak w istniejących plikach — nie komentować oczywistości.
- **DI**: wszystko rejestrowane w `App.OnStartup` (`App.xaml.cs`). Opcje budowane z `AppSettings`
  metodami `Create...Options`. Interfejsy w Core, implementacje w warstwach.
- **Testowalność**: wydzielaj czystą logikę (bez UI/Win32) do osobnych klas i testuj jednostkowo
  (wzorce: `VadSegmenter`, `TranscriptPostProcessor`, `OverlayPositioner`, `SentenceBoundary`,
  `UnicodeInputBuilder`, `SentenceRegistry`).
- **Config**: `AppSettings` (Core/Models) — pola tekstowe/proste; mapowane na enumy w App. Plik:
  `%AppData%\VoiceTypePL\config.json`. Bump `SchemaVersion` przy zmianach.
- **0 ostrzeżeń** utrzymywane — kompiluj czysto.
- Po każdym etapie: build 0/0, `dotnet test`, weryfikacja E2E, potem **krótki handoff i czekaj aż
  użytkownik zacommituje ręcznie** (nie commituj sam, chyba że poprosi).

---

## 4. Środowisko i uruchamianie

- **.NET 9 SDK**, projekty na net8.0 / net8.0-windows. Windows 11, PowerShell + Bash.
- **GPU**: jest RTX 3080, ale **brak CUDA toolkit** (`cublas`/`cudart`) → Whisper schodzi na CPU.
  Kryterium „<3 s (GPU)" wymaga instalacji runtime CUDA 12; kod jest GPU-ready (`WhisperOptions.PreferGpu`).
- **Model Whisper** pobierany przy 1. starcie do `%LocalAppData%\VoiceTypePL\models` (medium q5 ~514 MB).
- **Logi**: `%LocalAppData%\VoiceTypePL\logs\log-*.txt`.
- **Tryb headless** (weryfikacja bez mikrofonu): ustaw `VOICETYPEPL_AUDIO_FILE` na WAV 16 kHz mono →
  aplikacja przetworzy plik, zaloguje segmenty/transkrypcje/dymek i sama się zamknie.
  Exe: `src/VoiceTypePL.App/bin/Debug/net8.0-windows/VoiceTypePL.App.exe`.
- Polskie nagranie testowe generuje się przez `piper-tts` (patrz pamięć `headless-audio-verification`).

---

## 5. Jak weryfikować (wzorzec z dotychczasowych etapów)

1. **Build**: `dotnet build` → 0/0. **Testy**: `dotnet test`.
2. **Czysta logika** → testy jednostkowe w odpowiednim `*.Tests`.
3. **GUI/Win32/UIA** → **harness w scratchpadzie**: mały projekt konsolowy `net8.0-windows`
   referujący dany projekt, tworzy WŁASNE okno WPF (`TextBox`), wymusza foreground
   (`AttachThreadInput`+`SetForegroundWindow`) i steruje realnym API, po czym asertuje wynik.
   **Nigdy nie licz na `Topmost` = foreground** — inaczej SendInput/Ctrl+V trafi do cudzego okna
   (raz wkleiło do Notatnika użytkownika). Sprawdzaj `InjectionResult.WindowTitle == "własne okno"`.
4. **Manual-only** (do sprawdzenia przez użytkownika, jak headless-mikrofon): realny mikrofon,
   2 monitory/DPI/fokus (dymek), matryca aplikacji dla wpisywania/edycji (Notatnik/Word/Chrome/Slack).

---

## 6. Do poprawy / niedokończone (przed lub w trakcie kolejnych etapów)

- Użytkownik zgłosił „parę rzeczy do poprawy" — **dopytać o listę** na starcie sesji. Znane punkty niżej.
- **Dymek w trybie edycji nie pokazuje podglądu „stare → nowe"** (§5.6.4) — teraz tylko zaznaczenie + ramka.
- **Hover-edit** (bezruch ≥1,5 s) niezaimplementowany — jest tylko hotkey Ctrl+Alt+E (§5.6).
- Przycisk **„Nagraj ponownie"** to zaślepka (zwalnia slot; brak realnego re-dyktowania).
- **SentenceRegistry przy edycji** dopisuje nowe zdanie, ale nie aktualizuje/usuwa starego wpisu.
- **Schowek** przywraca tylko tekst (nie obrazy/inne formaty).
- **Aplikacje elevated (UIPI)** nie przyjmą inputu — brak opcji „uruchom jako admin" (§5.5).
- Jakość PL Whispera zależna od modelu (medium na CPU myli trudne słowa; turbo/GPU po instalacji CUDA).
- VAD na syntetycznym TTS sklejał zdania w jeden segment (realny mikrofon zwykle OK — sprawdzone przez usera).

---

## 7. Następne etapy (wg §7 dokumentacji)

### Etap 6 — EditEngine P2 + P3 (heurystyka kliknięć + tryb ręczny + cache per app + podgląd zmiany)
Punkty zaczepienia:
- `UiaSentenceLocator.LocateAndSelect` zwraca `null`, gdy pod kursorem brak `TextPattern` — **tam wpina
  się Poziom 2** (podwójny/potrójny klik + `Ctrl+Shift+←/→` z odczytem przez `Ctrl+C` do bufora tymcz.)
  i **Poziom 3** (użytkownik sam zaznacza, my tylko podmieniamy — już działa przez `ReplaceMode`).
- `EditModeService.OnEditHotkey` już obsługuje przypadek `null` (log „Poziom 1 niedostępny").
- **Cache poziomu per proces docelowy** (np. „notepad.exe → P1"): dane okna są w `InjectionResult`/
  `RegisteredSentence` (`ProcessName`).
- **Podgląd „stare → nowe"** w dymku (rozbudowa `OverlayViewModel`/`OverlayWindow`).

### Etap 7 — Szlif (ustawienia UI, autostart, czarna lista, filtr halucynacji, poziom sygnału)
- Okno ustawień (tray „Ustawienia…" jest wyłączone — `TrayIconService`). Przenieś `VadOptions`,
  `InjectionOptions`, `WhisperOptions`, `OverlayOptions` do UI + zapis przez `ISettingsService`.
- Autostart: klucz `HKCU\...\Run`. Czarna lista: `GetForegroundWindow` + auto-pauza (`AppState.IsPaused`).
- Filtr halucynacji już częściowo w `TranscriptPostProcessor` (rozbuduj listę/progi).
- Poziom sygnału: `AudioFrame.Rms` już liczony (podłącz do UI/ikony).

### Etap 8 — Pakowanie (self-contained publish, instalator Inno Setup/MSIX, first-run z modelem)
- Pobieranie modelu przy 1. starcie już działa (`WhisperModelProvider`).

---

## 8. Pamięć projektu (auto-wczytywana w nowej sesji)

W `…\memory\` jest indeks `MEMORY.md` + notatki z pułapkami — **przeczytaj je**:
- `headless-audio-verification` — tryb WAV + generowanie polskiego nagrania (piper).
- `whisper-gpu-needs-cuda-toolkit` — dlaczego GPU schodzi na CPU tutaj.
- `gitignore-models-pitfall` — nie ignorować katalogu `models/` (łapie źródłowy `Core/Models/`).
- `overlay-verification`, `injection-verification`, `edit-engine-verification` — jak testowane, znane bugi
  (m.in. klik zwijający zaznaczenie, `Topmost` ≠ foreground, `ClickToFocus` domyślnie wyłączony).

---

## 9. Pierwsze kroki nowej sesji

1. Przeczytaj `Dokumentacja-1.md` (§ docelowego etapu) i pamięć projektu.
2. Zapytaj użytkownika o „parę rzeczy do poprawy" (sekcja 6) — czy najpierw poprawki, czy Etap 6.
3. Zbuduj i odpal testy, by potwierdzić zielony start (66/66).
4. Pracuj etap-po-etapie: plan → weryfikacja założeń → implementacja z testami → E2E → handoff → czekaj na commit.
