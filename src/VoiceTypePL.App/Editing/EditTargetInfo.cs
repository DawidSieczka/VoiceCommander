using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoiceTypePL.App.Editing;

/// <summary>
/// Identyfikuje okno i proces pod kursorem — na potrzeby kaskady poziomów edycji (§5.6): cache poziomu
/// per proces oraz warunek Poziomu 3 (odczyt zaznaczenia tylko, gdy okno pod kursorem jest
/// pierwszoplanowe, żeby nie skopiować zaznaczenia z innego okna).
/// </summary>
internal static class EditTargetInfo
{
    /// <summary>Nazwa procesu (bez ścieżki/rozszerzenia) pod punktem ekranu (px) oraz czy to okno pierwszoplanowe.</summary>
    public static (string? ProcessName, bool IsForeground) Describe(int screenX, int screenY)
    {
        var point = new POINT { X = screenX, Y = screenY };
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
        {
            return (null, false);
        }

        // Okno pod kursorem vs pierwszoplanowe — porównujemy okna główne (root), bo WindowFromPoint zwraca
        // najgłębszą kontrolkę potomną.
        var rootUnderCursor = GetAncestor(hwnd, GA_ROOT);
        var foreground = GetForegroundWindow();
        var isForeground = rootUnderCursor != IntPtr.Zero && rootUnderCursor == foreground;

        GetWindowThreadProcessId(hwnd, out var pid);
        string? processName = null;
        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            // proces mógł zniknąć — nieistotne
        }

        return (processName, isForeground);
    }

    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
