using System.Runtime.InteropServices;

namespace VoiceTypePL.App.Overlay;

/// <summary>
/// Odczytuje pozycję kursora oraz obszar roboczy i DPI monitora, na którym kursor się znajduje —
/// dane wejściowe dla <see cref="OverlayPositioner"/>. Cienka warstwa nad <see cref="NativeMethods"/>.
/// </summary>
internal static class ScreenGeometry
{
    /// <summary>Pozycja kursora (px) i geometria monitora pod kursorem.</summary>
    public static (int CursorX, int CursorY, MonitorGeometry Monitor) CursorAndMonitor()
    {
        NativeMethods.GetCursorPos(out var pt);

        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(hMonitor, ref info);

        var dpi = 96.0;
        if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
        {
            dpi = dpiX;
        }

        var work = new PixelRect(
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Right - info.rcWork.Left,
            info.rcWork.Bottom - info.rcWork.Top);

        return (pt.X, pt.Y, new MonitorGeometry(work, dpi));
    }
}
