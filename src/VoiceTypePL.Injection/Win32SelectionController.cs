using Microsoft.Extensions.Logging;
using VoiceTypePL.Core.Injection;
using static VoiceTypePL.Injection.InjectionNativeMethods;
using Clipboard = System.Windows.Clipboard;

namespace VoiceTypePL.Injection;

/// <summary>
/// Sterowanie zaznaczeniem w cudzym oknie przez Win32 (§5.6, Poziomy 2/3): syntetyczne kliknięcia
/// (dwuklik/trzyklik), ruchy karetki i rozszerzanie zaznaczenia klawiaturą oraz niezawodny odczyt
/// bieżącego zaznaczenia techniką sentinela w schowku. Działa na wątku UI (STA — wymóg schowka WPF).
/// </summary>
public sealed class Win32SelectionController : ISelectionController
{
    // Czas na odczyt schowka przez okno docelowe po Ctrl+C, zanim odczytamy wynik i przywrócimy schowek.
    private const int ClipboardSettleMs = 60;

    private readonly ILogger<Win32SelectionController>? _logger;

    public Win32SelectionController(ILogger<Win32SelectionController>? logger = null)
    {
        _logger = logger;
    }

    public async Task<string?> ReadSelectionAsync(CancellationToken cancellationToken = default)
    {
        // Unikalny znacznik: jeśli po Ctrl+C nadal jest w schowku, znaczy że nic nie było zaznaczone
        // (Ctrl+C nie nadpisało schowka). Dzięki temu odróżniamy „brak zaznaczenia" od pustego tekstu.
        var sentinel = "VTPL_SEL_" + Guid.NewGuid().ToString("N");

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
            _logger?.LogDebug(ex, "Nie udało się odczytać schowka przed odczytem zaznaczenia.");
        }

        if (!TrySetClipboardText(sentinel))
        {
            return null;
        }

        SendChord(VK_CONTROL, VK_C);
        await Task.Delay(ClipboardSettleMs, cancellationToken).ConfigureAwait(true);

        string? current = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                current = Clipboard.GetText();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Nie udało się odczytać schowka po Ctrl+C.");
        }

        RestoreClipboard(saved);

        // Brak zaznaczenia: Ctrl+C nic nie skopiowało, sentinel przetrwał.
        if (current is null || current == sentinel)
        {
            return null;
        }

        return current;
    }

    public void Click(int screenX, int screenY)
    {
        // Klik w bezwzględnych, znormalizowanych współrzędnych wirtualnego pulpitu (0..65535) — pewniejsze
        // niż klik względny w bieżącej pozycji: zdarzenie samo niesie punkt i trafia niezależnie od DPI/monitora.
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        var nx = (int)Math.Round((screenX - vx) * 65535.0 / Math.Max(1, vw - 1));
        var ny = (int)Math.Round((screenY - vy) * 65535.0 / Math.Max(1, vh - 1));

        const uint abs = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        Send(new[]
        {
            MouseInput(MOUSEEVENTF_MOVE | abs, nx, ny),
            MouseInput(MOUSEEVENTF_LEFTDOWN | abs, nx, ny),
            MouseInput(MOUSEEVENTF_LEFTUP | abs, nx, ny),
        });
    }

    public void MoveToLineStart() => SendKey(VK_HOME, 1);

    public void ExtendToLineStart() => SendChord(VK_SHIFT, VK_HOME);

    public void ExtendToLineEnd() => SendChord(VK_SHIFT, VK_END);

    public void MoveCaretRight(int steps) => SendKey(VK_RIGHT, steps);

    public void ExtendRight(int steps) => SendChordRepeat(VK_SHIFT, VK_RIGHT, steps);

    /// <summary>Wysyła pojedynczy klawisz <paramref name="count"/> razy (down+up).</summary>
    private static void SendKey(ushort virtualKey, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var inputs = new INPUT[count * 2];
        for (var i = 0; i < count; i++)
        {
            inputs[i * 2] = KeyInput(virtualKey, keyUp: false);
            inputs[i * 2 + 1] = KeyInput(virtualKey, keyUp: true);
        }

        SendChunked(inputs);
    }

    /// <summary>Wysyła akord modyfikator+klawisz jednokrotnie (np. Ctrl+C).</summary>
    private static void SendChord(ushort modifier, ushort key)
    {
        var inputs = new[]
        {
            KeyInput(modifier, keyUp: false),
            KeyInput(key, keyUp: false),
            KeyInput(key, keyUp: true),
            KeyInput(modifier, keyUp: true),
        };
        Send(inputs);
    }

    /// <summary>Trzyma modyfikator i naciska klawisz <paramref name="count"/> razy (np. Shift+Right×n).</summary>
    private static void SendChordRepeat(ushort modifier, ushort key, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var inputs = new INPUT[count * 2 + 2];
        inputs[0] = KeyInput(modifier, keyUp: false);
        for (var i = 0; i < count; i++)
        {
            inputs[1 + i * 2] = KeyInput(key, keyUp: false);
            inputs[2 + i * 2] = KeyInput(key, keyUp: true);
        }

        inputs[^1] = KeyInput(modifier, keyUp: true);
        SendChunked(inputs);
    }

    /// <summary>Wysyła w porcjach — długie sekwencje bywają obcinane przy jednym SendInput.</summary>
    private static void SendChunked(INPUT[] inputs)
    {
        const int chunk = 400;
        for (var offset = 0; offset < inputs.Length; offset += chunk)
        {
            Send(inputs[offset..Math.Min(offset + chunk, inputs.Length)]);
        }
    }

    private bool TrySetClipboardText(string text)
    {
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

        _logger?.LogWarning("Nie udało się ustawić schowka — odczyt zaznaczenia nieudany.");
        return false;
    }

    private void RestoreClipboard(string? saved)
    {
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
            _logger?.LogDebug(ex, "Nie udało się przywrócić schowka po odczycie zaznaczenia.");
        }
    }
}
