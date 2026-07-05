using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.Injection;
using static VoiceTypePL.Injection.InjectionNativeMethods;
using Clipboard = System.Windows.Clipboard;

namespace VoiceTypePL.Injection;

/// <summary>
/// Wstrzykiwanie tekstu przez Win32 (§5.5): opcjonalny klik w pozycji kursora (gdy brak karetki),
/// a następnie wklejenie schowkiem (Ctrl+V) z przywróceniem poprzedniej zawartości albo wpisanie
/// Unicode SendInput. Pola hasła są pomijane (UIA <c>IsPassword</c>). Metoda działa na wątku UI (STA),
/// bo tego wymaga schowek WPF.
/// </summary>
public sealed class Win32TextInjector : ITextInjector
{
    private readonly InjectionOptions _options;
    private readonly ILogger<Win32TextInjector>? _logger;

    public Win32TextInjector(InjectionOptions options, ILogger<Win32TextInjector>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<InjectionResult> InjectAsync(
        string text,
        bool? appendSpace = null,
        bool? clickToFocus = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return InjectionResult.Skipped("pusty tekst", _options.Strategy);
        }

        if (!GetCursorPos(out var cursor))
        {
            cursor = default;
        }

        if (_options.SkipPasswordFields && IsPasswordAt(cursor))
        {
            _logger?.LogInformation("Pominięto wstrzyknięcie — kursor nad polem hasła.");
            return InjectionResult.Skipped("pole hasła", _options.Strategy);
        }

        // Klik ustawia fokus/karetkę tylko gdy nie ma już aktywnej karetki (§5.5 krok 2). W trybie
        // edycji klik jest wyłączony (clickToFocus=false) — inaczej zwinąłby zaznaczenie do nadpisania
        // i wstawił tekst w pozycji myszy zamiast podmienić zdanie.
        if ((clickToFocus ?? _options.ClickToFocus) && !HasActiveCaret())
        {
            ClickAtCursor();
            await Task.Delay(40, cancellationToken).ConfigureAwait(true);   // pozwól fokusowi się ustabilizować
        }

        var (title, processName, processId) = DescribeForeground();
        var payload = (appendSpace ?? _options.AppendSpace) ? text + " " : text;

        bool ok;
        try
        {
            ok = _options.Strategy == InjectionStrategy.UnicodeSendInput
                ? SendUnicode(payload)
                : await PasteViaClipboardAsync(payload, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Błąd wstrzykiwania tekstu.");
            return new InjectionResult(false, _options.Strategy, title, processName, processId, "wyjątek");
        }

        _logger?.LogInformation(
            "Wstrzyknięto ({Strategy}) do okna {Title} [{Proc}]: {Chars} znaków.",
            _options.Strategy, title, processName, payload.Length);

        return new InjectionResult(ok, _options.Strategy, title, processName, processId);
    }

    private async Task<bool> PasteViaClipboardAsync(string payload, CancellationToken cancellationToken)
    {
        string? saved = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                saved = Clipboard.GetText();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Nie udało się odczytać schowka przed wklejeniem.");
        }

        if (!TrySetClipboardText(payload))
        {
            return false;
        }

        SendCtrlV();

        // Daj celowi czas na odczyt schowka, potem przywróć poprzednią zawartość.
        await Task.Delay(_options.ClipboardRestoreDelayMs, cancellationToken).ConfigureAwait(true);

        try
        {
            if (saved is not null)
            {
                Clipboard.SetText(saved);
            }
            else
            {
                Clipboard.Clear();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Nie udało się przywrócić schowka.");
        }

        return true;
    }

    private bool TrySetClipboardText(string text)
    {
        // Schowek bywa chwilowo zablokowany przez inny proces — kilka prób.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Schowek zajęty — próba {Attempt}.", attempt + 1);
                Thread.Sleep(25);
            }
        }

        _logger?.LogWarning("Nie udało się ustawić schowka — wstrzyknięcie przez schowek nieudane.");
        return false;
    }

    private static void ClickAtCursor()
    {
        var inputs = new[]
        {
            MouseInput(MOUSEEVENTF_LEFTDOWN),
            MouseInput(MOUSEEVENTF_LEFTUP),
        };
        Send(inputs);
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyInput(VK_CONTROL, keyUp: false),
            KeyInput(VK_V, keyUp: false),
            KeyInput(VK_V, keyUp: true),
            KeyInput(VK_CONTROL, keyUp: true),
        };
        Send(inputs);
    }

    private static bool SendUnicode(string text)
    {
        var strokes = UnicodeInputBuilder.Build(text);
        var inputs = new INPUT[strokes.Count];
        for (var i = 0; i < strokes.Count; i++)
        {
            inputs[i] = UnicodeInput(strokes[i].CodeUnit, strokes[i].KeyUp);
        }

        // Wysyłamy w porcjach — bardzo długie sekwencje bywają obcinane przy jednym wywołaniu.
        const int chunk = 400;
        var sent = 0u;
        for (var offset = 0; offset < inputs.Length; offset += chunk)
        {
            var slice = inputs[offset..Math.Min(offset + chunk, inputs.Length)];
            sent += Send(slice);
        }

        return sent == (uint)inputs.Length;
    }

    private static bool HasActiveCaret()
    {
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(0, ref info) && info.hwndCaret != IntPtr.Zero;
    }

    private bool IsPasswordAt(POINT point)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            return element?.Current.IsPassword ?? false;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "UIA IsPassword niedostępne dla punktu kursora — zakładam brak hasła.");
            return false;
        }
    }

    private static (string? Title, string? ProcessName, int ProcessId) DescribeForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return (null, null, 0);
        }

        GetWindowThreadProcessId(hwnd, out var pid);

        var buffer = new StringBuilder(256);
        GetWindowText(hwnd, buffer, buffer.Capacity);

        string? processName = null;
        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            // proces mógł zniknąć — nieistotne dla rejestru
        }

        return (buffer.ToString(), processName, (int)pid);
    }

    private static INPUT KeyInput(ushort virtualKey, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = 0,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = 0,
            },
        },
    };

    private static INPUT UnicodeInput(ushort codeUnit, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = codeUnit,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = 0,
            },
        },
    };

    private static INPUT MouseInput(uint flags) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = 0,
            },
        },
    };
}
