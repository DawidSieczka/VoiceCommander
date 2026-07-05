# Plan implementacji: „VoiceType PL" — dyktowanie głosowe dla Windows
**Stos: C# / .NET 8 (WPF) + Whisper.net (lokalny STT)**

---

## 1. Cel i zakres

Aplikacja desktopowa dla Windows, która:

1. Nasłuchuje mikrofonu w sposób ciągły (praca w tle, ikona w zasobniku systemowym).
2. Po wykryciu zakończonego zdania wyświetla dymek (overlay) z transkrypcją i przyciskami **Zatwierdź / Odrzuć / Popraw**.
3. Po zatwierdzeniu „wpisuje" tekst w miejscu, w którym znajduje się kursor myszy — w dowolnym oknie, na dowolnym monitorze.
4. Pozwala edytować wcześniej wpisane zdanie: użytkownik najeżdża na nie kursorem, aktywuje tryb edycji, dyktuje nową wersję — podmiana dotyczy wyłącznie wskazanego zdania.
5. Pełna obsługa języka polskiego (transkrypcja, interpunkcja, polskie znaki).

---

## 2. Architektura wysokopoziomowa

```
┌────────────────────────────────────────────────────────────┐
│                      VoiceType PL (WPF)                     │
│                                                             │
│  ┌──────────────┐   ┌──────────────┐   ┌────────────────┐  │
│  │ AudioCapture │──▶│ VadSegmenter │──▶│ Transcriber    │  │
│  │ (NAudio/WASAPI)  │ (Silero VAD) │   │ (Whisper.net)  │  │
│  └──────────────┘   └──────────────┘   └───────┬────────┘  │
│                                                │ zdanie     │
│                                                ▼            │
│  ┌──────────────┐   ┌──────────────────────────────────┐   │
│  │ TrayIcon /   │   │ OverlayService (dymek WPF,       │   │
│  │ Settings     │   │ topmost, per-monitor DPI)        │   │
│  └──────────────┘   └──────────────┬───────────────────┘   │
│                                    │ zatwierdzono           │
│                                    ▼                        │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ TextInjector (klik w pozycji myszy → fokus →         │  │
│  │ wklejenie przez schowek / SendInput)                 │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ EditEngine (UI Automation TextPattern → fallbacki)   │  │
│  │ + SentenceRegistry (historia wpisanych zdań)         │  │
│  └──────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────┘
```

Wszystkie moduły komunikują się przez wewnętrzny event bus (zwykłe C# events lub `System.Threading.Channels`), dzięki czemu STT można w przyszłości podmienić (np. na Azure) bez ruszania reszty.

---

## 3. Struktura projektu

```
VoiceTypePL.sln
├── src/
│   ├── VoiceTypePL.App/            # WPF: tray, overlay, ustawienia
│   │   ├── Overlay/                #   dymek potwierdzenia
│   │   ├── Tray/                   #   ikona + menu zasobnika
│   │   └── Settings/               #   okno ustawień (JSON config)
│   ├── VoiceTypePL.Audio/          # NAudio: przechwytywanie WASAPI
│   ├── VoiceTypePL.Vad/            # Silero VAD (ONNX Runtime)
│   ├── VoiceTypePL.Stt/            # Whisper.net + zarządzanie modelami
│   ├── VoiceTypePL.Injection/      # SendInput, schowek, klik myszy
│   ├── VoiceTypePL.EditEngine/     # UIA TextPattern + fallbacki
│   └── VoiceTypePL.Core/           # modele, eventy, SentenceRegistry
└── tests/
    ├── VoiceTypePL.Stt.Tests/
    └── VoiceTypePL.EditEngine.Tests/
```

---

## 4. Kluczowe biblioteki (NuGet)

| Obszar | Pakiet | Uwagi |
|---|---|---|
| Audio | `NAudio` | WASAPI capture, 16 kHz mono PCM dla Whispera |
| VAD | `Microsoft.ML.OnnxRuntime` + model Silero VAD | lekki, dokładny; alternatywa: `WebRtcVadSharp` (prostszy, mniej dokładny) |
| STT | `Whisper.net` + `Whisper.net.Runtime` | wariant runtime: CPU / `Cuda` / `Vulkan` — wybierany przy starcie wg sprzętu |
| UIA | `System.Windows.Automation` (wbudowane) lub CsWin32/interop do **UIA3 (IUIAutomation COM)** | UIA3 zalecane — nowsze API, lepsze wsparcie przeglądarek |
| Wstrzykiwanie | P/Invoke `SendInput`, `SetForegroundWindow`, schowek WPF | bez zewnętrznych zależności |
| Hotkeye/hooki | P/Invoke `RegisterHotKey`, `SetWindowsHookEx(WH_MOUSE_LL)` | globalny skrót + śledzenie myszy |
| Tray | `H.NotifyIcon.Wpf` | ikona zasobnika w WPF |
| Config/logi | `System.Text.Json`, `Serilog` | |

Model Whisper: **ggml `large-v3-turbo` (q5)** jako domyślny przy GPU, **`medium` (q5)** przy CPU — najlepszy kompromis jakości polskiego i szybkości. Pobieranie modelu przy pierwszym uruchomieniu (z paskiem postępu), zapis do `%LocalAppData%\VoiceTypePL\models`.

---

## 5. Moduły — szczegóły implementacji

### 5.1 AudioCapture (NAudio)
- `WasapiCapture` na domyślnym urządzeniu wejściowym; resampling do 16 kHz / 16-bit / mono (`MediaFoundationResampler`).
- Bufor pierścieniowy ~30 s; audio płynie do VAD w ramkach 30 ms.
- Obsługa zmiany domyślnego mikrofonu w locie (`MMDeviceEnumerator` + notyfikacje).
- Wskaźnik poziomu sygnału (do UI) liczony z RMS ramek.

### 5.2 VadSegmenter (Silero VAD)
- Wykrywa mowę/ciszę na ramkach; **koniec zdania = cisza ≥ 700 ms** (konfigurowalne 400–1200 ms).
- Skleja ramki mowy w segment (z 250 ms paddingu przed i po), emituje event `SpeechSegmentReady(float[] pcm)`.
- Zabezpieczenia: minimalna długość segmentu 300 ms (odcina stuki), maksymalna 30 s (przycięcie i wysłanie).

### 5.3 Transcriber (Whisper.net)
- Jedna instancja `WhisperFactory` (model trzymany w pamięci), kolejka segmentów (`Channel<T>`), przetwarzanie sekwencyjne na osobnym wątku.
- Parametry: `Language("pl")`, temperatura 0, `WithoutTimestamps` wyłączone tylko jeśli niepotrzebne.
- Detekcja runtime: próba CUDA → Vulkan → CPU (Whisper.net.Runtime ładuje odpowiednią natywkę).
- Post-processing: trim, kapitalizacja pierwszej litery, dopięcie kropki jeśli brak interpunkcji końcowej, filtr halucynacji na ciszy (odrzucenie znanych artefaktów typu „Napisy stworzone przez…", segmenty o niskim `avg_logprob`).
- Emituje `SentenceTranscribed(string text, TimeSpan audioLen)`.

### 5.4 OverlayService (dymek)
- Okno WPF: `Topmost=true`, `WindowStyle=None`, `AllowsTransparency=true`, `ShowActivated=false` + rozszerzony styl `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` — **dymek nie kradnie fokusa** (krytyczne: fokus musi zostać w docelowym oknie).
- Pozycjonowanie przy kursorze myszy z korektą per-monitor DPI (`GetDpiForMonitor`, manifest `PerMonitorV2`) i clampingiem do krawędzi ekranu.
- Zawartość: transkrypcja (edytowalny `TextBox`), przyciski **Zatwierdź (Enter)**, **Odrzuć (Esc)**, **Nagraj ponownie**; auto-ukrycie po N sekundach (konfigurowalne, opcjonalnie wyłączone).
- Obsługa też w pełni głosowa/hotkeyowa, żeby nie trzeba było ruszać myszy: globalny Enter/Esc aktywny tylko gdy dymek widoczny (low-level keyboard hook z pochłonięciem zdarzenia).
- Kolejka: jeśli użytkownik dyktuje szybciej niż zatwierdza, zdania czekają w kolejce widocznej w dymku (licznik „+2 oczekujące").

### 5.5 TextInjector
Sekwencja „wpisania" po zatwierdzeniu:
1. Odczyt pozycji kursora myszy (`GetCursorPos`) i okna pod nim (`WindowFromPoint`).
2. `SendInput` z kliknięciem LPM w tej pozycji → ustawia fokus i karetkę w docelowej kontrolce (klik pomijany, jeśli okno pod kursorem już ma fokus i karetka jest aktywna — heurystyka przez `GUITHREADINFO.hwndCaret`).
3. Wstrzyknięcie tekstu — dwie strategie, wybierane w ustawieniach:
   - **Schowek (domyślna):** zapis obecnej zawartości schowka → `SetText` → `Ctrl+V` przez `SendInput` → przywrócenie schowka po 150 ms. Szybkie, w pełni odporne na polskie znaki i układ klawiatury.
   - **Unicode SendInput:** `KEYEVENTF_UNICODE` znak po znaku. Wolniejsze, ale działa tam, gdzie aplikacja blokuje wklejanie.
4. Po wpisaniu: rejestracja zdania w `SentenceRegistry` (patrz 5.7) + dopisanie spacji separującej.

Uwagi: aplikacje z uprawnieniami administratora nie przyjmą inputu od procesu bez elevacji (UIPI) — w ustawieniach opcja „uruchamiaj jako administrator"; pola hasła są ignorowane (wykrycie przez UIA `IsPassword`).

### 5.6 EditEngine — edycja zdania pod kursorem
Aktywacja trybu edycji: użytkownik najeżdża kursorem na zdanie i wciska **globalny hotkey** (domyślnie `Ctrl+Alt+E`) *lub* przytrzymuje kursor nieruchomo ≥ 1,5 s przy włączonym trybie „hover-edit" (low-level mouse hook mierzy bezruch). Hotkey jest domyślny — czyste najechanie generowałoby zbyt wiele fałszywych aktywacji.

Trzy poziomy realizacji, próbowane kolejno:

**Poziom 1 — UI Automation TextPattern (pełna precyzja):**
1. `IUIAutomation.ElementFromPoint(pt)` → element pod kursorem.
2. Pobranie `TextPattern` (z elementu lub przodków) → `RangeFromPoint(pt)` → zakres znakowy pod kursorem.
3. Ekspansja zakresu do zdania: `ExpandToEnclosingUnit(TextUnit.Sentence)`; jeśli aplikacja nie wspiera jednostki „Sentence" — ekspansja ręczna: rozszerzanie po znaku w obu kierunkach do `. ! ? \n` z limitem 500 znaków.
4. Podświetlenie: `range.Select()` (użytkownik widzi zaznaczenie) + ramka overlay na `GetBoundingRectangles()`.
5. Po nadyktowaniu nowej wersji i zatwierdzeniu: zaznaczenie jest aktywne → wstrzyknięcie tekstu (5.5) nadpisuje wyłącznie ten zakres.
- Działa: Notatnik, WordPad, Word, Outlook, pola tekstowe Win32/WinForms/WPF, Chrome/Edge/Firefox (contenteditable i `<textarea>` przez IAccessible2/UIA bridge).

**Poziom 2 — heurystyka kliknięć (częściowa precyzja):**
- Gdy brak TextPattern: podwójny klik w pozycji kursora zaznacza słowo; następnie rozszerzanie zaznaczenia `Ctrl+Shift+→ / ←` z odczytem zaznaczenia przez `Ctrl+C` do bufora tymczasowego (z przywróceniem schowka), aż do granic zdania. Wolniejsze i widoczne dla użytkownika, ale skuteczne w większości edytorów.
- Wariant uproszczony: triple-click = zaznaczenie całej linii/akapitu (użytkownik informowany, że podmieni całą linię).

**Poziom 3 — tryb ręczny (uniwersalny):**
- Użytkownik sam zaznacza tekst myszą, wciska hotkey edycji, dyktuje — aplikacja podmienia bieżące zaznaczenie. Działa wszędzie, gdzie działa Ctrl+V.

Wynik detekcji poziomu jest cache'owany per proces docelowy (np. „notepad.exe → Poziom 1"), a w dymku edycji zawsze widać, **co dokładnie zostanie podmienione** (podgląd starego → nowego tekstu) przed zatwierdzeniem.

### 5.7 SentenceRegistry
- Historia wpisanych zdań: tekst, timestamp, HWND + tytuł okna docelowego, PID, użyty poziom wstrzyknięcia.
- Rola pomocnicza: (a) walidacja przy edycji — porównanie tekstu zaznaczonego przez EditEngine z historią (fuzzy match ≥ 85%) i ostrzeżenie, jeśli użytkownik celuje w tekst, którego aplikacja nie wpisała; (b) panel „ostatnie zdania" w ustawieniach z możliwością ponownego wstawienia.
- Świadome ograniczenie: rejestr **nie śledzi pozycji** tekstu po wpisaniu (scroll/edycja ręczna je unieważnia) — pozycję zawsze wyznacza kursor myszy w momencie edycji. To decyzja architektoniczna, nie brak.

### 5.8 Tray, ustawienia, hotkeye
- Ikona zasobnika: stan (nasłuch / pauza / przetwarzanie), szybkie menu (Pauza `Ctrl+Alt+M`, Ustawienia, Zamknij).
- Ustawienia (JSON w `%AppData%`): wybór mikrofonu, model Whispera, próg ciszy VAD, strategia wstrzykiwania, hotkeye, autostart z Windows (klucz w `HKCU\...\Run`), tryb hover vs hotkey dla edycji, czas auto-ukrycia dymka.
- Tryb pauzy automatycznej, gdy aktywna aplikacja jest na czarnej liście (np. gry pełnoekranowe — detekcja przez `GetForegroundWindow`).

---

## 6. Przepływy (scenariusze)

**Dyktowanie:**
mowa → VAD wykrywa koniec → Whisper transkrybuje → dymek przy kursorze → Enter → klik w pozycji myszy → wklejenie → rejestracja zdania.

**Edycja:**
najechanie na zdanie → `Ctrl+Alt+E` → EditEngine zaznacza zdanie (poziom 1/2/3) + ramka podświetlenia → dymek pokazuje „stare → (dyktuj nowe)" → mowa → transkrypcja w dymku → Enter → podmiana zaznaczenia → aktualizacja rejestru. Esc w dowolnym momencie zdejmuje zaznaczenie bez zmian.

---

## 7. Etapy realizacji (milestones)

| # | Etap | Zakres | Kryterium ukończenia | Szac. czas* |
|---|---|---|---|---|
| 0 | Szkielet | Solution, DI, logging, tray, config | Aplikacja startuje do zasobnika | 0,5–1 dzień |
| 1 | Audio + VAD | NAudio 16 kHz, Silero, segmentacja | Log pokazuje poprawne segmenty zdań | 1–2 dni |
| 2 | STT | Whisper.net, pobieranie modelu, PL, post-processing | Poprawna transkrypcja polskich zdań < 3 s (GPU) / < 8 s (CPU, medium) | 1–2 dni |
| 3 | Dymek | Overlay no-activate, DPI, Enter/Esc, kolejka | Dymek działa na 2 monitorach o różnym DPI, nie kradnie fokusa | 2–3 dni |
| 4 | Wstrzykiwanie | Klik + schowek + SendInput Unicode, przywracanie schowka | Wpisywanie działa w Notatniku, Chrome, Word, Slack | 1–2 dni |
| 5 | EditEngine P1 | UIA3, RangeFromPoint, ekspansja do zdania, podświetlenie | Edycja zdania działa w Notatniku, Wordzie i Chrome | 3–5 dni |
| 6 | EditEngine P2+P3 | Heurystyka kliknięć, tryb ręczny, cache per aplikacja, podgląd zmiany | Sensowne zachowanie w aplikacji bez UIA | 2–3 dni |
| 7 | Szlif | Ustawienia UI, autostart, czarna lista, filtr halucynacji, poziom sygnału | Beta gotowa do codziennego użytku | 2–4 dni |
| 8 | Pakowanie | Publish self-contained, instalator (Inno Setup / MSIX), first-run z pobraniem modelu | Instalator .exe na czystym Windows 10/11 | 1–2 dni |

\* przy jednym doświadczonym programiście C#; łącznie ~3–4 tygodnie kalendarzowo z testami.

Kolejność 1→4 daje **działający dyktafon end-to-end już po etapie 4** — edycja (5–6) jest nakładką.

---

## 8. Ryzyka i mitygacje

| Ryzyko | Prawdopod. | Mitygacja |
|---|---|---|
| Aplikacja docelowa bez wsparcia UIA TextPattern | Wysokie (część aplikacji) | Poziomy 2 i 3, cache per aplikacja, jasny komunikat w dymku |
| Latencja Whispera na słabym CPU | Średnie | Model `small`/`medium` q5, opcja podmiany na Azure w przyszłości (interfejs `ITranscriber`) |
| Halucynacje Whispera na ciszy/szumie | Średnie | VAD odcina ciszę, filtr znanych artefaktów, próg `no_speech_prob` |
| `SetForegroundWindow` blokowane przez Windows | Niskie/średnie | Klik przez `SendInput` naturalnie przenosi fokus; fallback `AttachThreadInput` |
| Konflikt ze schowkiem użytkownika | Średnie | Zapis/odtworzenie schowka, opcja trybu SendInput Unicode |
| Okna elevated (admin) nie przyjmują inputu | Niskie | Opcja uruchamiania z elevacją + informacja w dymku |
| Duży rozmiar dystrybucji (model 0,8–1,5 GB) | Pewne | Model pobierany osobno przy pierwszym starcie, wybór rozmiaru |

---

## 9. Testy

- **Jednostkowe:** segmentacja VAD (nagrania referencyjne PL), post-processing transkrypcji, ekspansja zakresu do zdania (mocki UIA), fuzzy match rejestru.
- **Integracyjne (ręczne, macierz aplikacji):** Notatnik, Word, Excel, Chrome (Gmail, Google Docs), Firefox, VS Code, Slack/Teams, stara aplikacja Win32 — dla każdej: wpisywanie ✓/✗, edycja P1/P2/P3.
- **Środowiska:** Windows 10 22H2 i Windows 11, monitor 100% + 150% DPI, 1 i 2 monitory, CPU-only i GPU NVIDIA.
- **Językowe:** zestaw ~50 zdań PL (znaki diakrytyczne, liczby, nazwy własne) — pomiar WER dla wybranego modelu.

---

## 10. Możliwe rozszerzenia (poza MVP)

- Komendy głosowe („nowa linia", „usuń ostatnie zdanie", „wielka litera").
- Streaming częściowej transkrypcji w dymku w trakcie mówienia (whisper na oknach przesuwnych).
- Podmienny backend STT (Azure) dla trybu online o niższej latencji.
- Słownik użytkownika / autokorekta nazw własnych po transkrypcji.
- Profil per aplikacja (np. inna strategia wstrzykiwania dla VS Code).
