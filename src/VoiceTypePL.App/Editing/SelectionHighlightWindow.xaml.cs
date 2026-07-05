using System.Windows;
using System.Windows.Interop;
using VoiceTypePL.App.Overlay;

namespace VoiceTypePL.App.Editing;

/// <summary>
/// Przezroczysta, klikalna „na wylot" ramka podświetlająca zaznaczone zdanie (§5.6.4). Nie przejmuje
/// fokusa (WS_EX_NOACTIVATE) i nie łapie myszy (WS_EX_TRANSPARENT), więc nie przeszkadza w edycji.
/// Pozycjonowana w fizycznych pikselach wg prostokątów z UIA.
/// </summary>
public partial class SelectionHighlightWindow : Window
{
    private const int FramePadding = 2;

    public SelectionHighlightWindow()
    {
        InitializeComponent();
    }

    /// <summary>Pokazuje ramkę wokół sumy podanych prostokątów (px ekranu). Pusta lista = ukryj.</summary>
    public void ShowAt(IReadOnlyList<Rect> rectangles)
    {
        if (rectangles.Count == 0)
        {
            HideFrame();
            return;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var r in rectangles)
        {
            left = Math.Min(left, r.Left);
            top = Math.Min(top, r.Top);
            right = Math.Max(right, r.Right);
            bottom = Math.Max(bottom, r.Bottom);
        }

        var x = (int)left - FramePadding;
        var y = (int)top - FramePadding;
        var width = (int)(right - left) + FramePadding * 2;
        var height = (int)(bottom - top) + FramePadding * 2;

        if (!IsVisible)
        {
            Show();
        }

        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER);
    }

    /// <summary>Ukrywa ramkę.</summary>
    public void HideFrame() => Hide();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, exStyle);
    }
}
