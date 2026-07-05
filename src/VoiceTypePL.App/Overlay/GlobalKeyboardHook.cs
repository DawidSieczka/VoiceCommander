using System.Runtime.InteropServices;

namespace VoiceTypePL.App.Overlay;

/// <summary>
/// Globalny low-level hook klawiatury (WH_KEYBOARD_LL) łapiący Enter/Esc, gdy dymek jest widoczny
/// (§5.4: „globalny Enter/Esc aktywny tylko gdy dymek widoczny … z pochłonięciem zdarzenia"). Gdy
/// <see cref="IsEnabled"/> = true, zdarzenie jest konsumowane (nie dociera do aktywnego okna).
/// Hook trzeba zainstalować z wątku z pętlą komunikatów (wątek UI) — callback biegnie na tym wątku.
/// </summary>
internal sealed class GlobalKeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;   // trzymamy referencję, żeby GC nie zebrał delegata podpiętego do Win32
    }

    /// <summary>Gdy true — Enter/Esc są łapane i pochłaniane; gdy false — hook przepuszcza wszystko.</summary>
    public bool IsEnabled { get; set; }

    public event Action? EnterPressed;
    public event Action? EscapePressed;

    public void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            var message = (int)wParam;
            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                switch (info.vkCode)
                {
                    case NativeMethods.VK_RETURN:
                        EnterPressed?.Invoke();
                        return 1;               // pochłoń — nie przekazuj do aktywnego okna
                    case NativeMethods.VK_ESCAPE:
                        EscapePressed?.Invoke();
                        return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
