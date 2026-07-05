using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VoiceTypePL.App.Editing;

/// <summary>
/// Globalny skrót systemowy (§5.6/§5.8) przez <c>RegisterHotKey</c>. Odbiera <c>WM_HOTKEY</c> przez
/// ukryte okno <see cref="HwndSource"/> na wątku UI i zgłasza <see cref="Pressed"/> na tym wątku.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly int _id;
    private HwndSource? _source;

    public GlobalHotkey(int id = 0x5643)   // dowolne, stałe id skrótu
    {
        _id = id;
    }

    /// <summary>Zgłaszane po wciśnięciu skrótu (na wątku UI).</summary>
    public event Action? Pressed;

    /// <summary>Rejestruje skrót. Zwraca false, gdy zajęty przez inną aplikację.</summary>
    public bool Register(uint modifiers, uint virtualKey)
    {
        _source ??= CreateMessageWindow();
        return RegisterHotKey(_source.Handle, _id, modifiers | MOD_NOREPEAT, virtualKey);
    }

    private HwndSource CreateMessageWindow()
    {
        var source = new HwndSource(new HwndSourceParameters("VoiceTypePL.Hotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        source.AddHook(WndProc);
        return source;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            Pressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            UnregisterHotKey(_source.Handle, _id);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
