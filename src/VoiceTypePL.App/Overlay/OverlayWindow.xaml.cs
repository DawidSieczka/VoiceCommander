using System.Windows;
using System.Windows.Interop;

namespace VoiceTypePL.App.Overlay;

/// <summary>
/// Okno dymka (§5.4). Krytyczne: NIE kradnie fokusa — rozszerzone style
/// <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> + <c>ShowActivated=false</c>, a pozycjonowanie idzie
/// przez <c>SetWindowPos</c> z <c>SWP_NOACTIVATE</c> w fizycznych pikselach (poprawne między monitorami
/// o różnym DPI). Zawartość i logika kolejki są w <see cref="OverlayViewModel"/> / OverlayService —
/// okno tylko renderuje i zgłasza akcje użytkownika.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        ConfirmButton.Click += (_, _) => Confirm();
        RejectButton.Click += (_, _) => Reject();
        RerecordButton.Click += (_, _) => Rerecord();
    }

    /// <summary>Zatwierdzenie bieżącego zdania (Enter / przycisk). Niesie aktualny — może edytowany — tekst.</summary>
    public event EventHandler<string>? ConfirmRequested;

    /// <summary>Odrzucenie bieżącego zdania (Esc / przycisk).</summary>
    public event EventHandler? RejectRequested;

    /// <summary>Prośba o ponowne nagranie (przycisk).</summary>
    public event EventHandler? RerecordRequested;

    public void Confirm() => ConfirmRequested?.Invoke(this, _viewModel.Text);

    public void Reject() => RejectRequested?.Invoke(this, EventArgs.Empty);

    public void Rerecord() => RerecordRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Pokazuje dymek bez przejmowania fokusa i pozycjonuje go przy kursorze.</summary>
    public void ShowNoActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        // Layout musi się policzyć, żeby znać rozmiar do pozycjonowania.
        UpdateLayout();
        PositionAtCursor();
    }

    /// <summary>Ustawia dymek przy kursorze na właściwym monitorze (fizyczne piksele, bez aktywacji).</summary>
    public void PositionAtCursor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var (cursorX, cursorY, monitor) = ScreenGeometry.CursorAndMonitor();

        // Rozmiar okna w DIP-ach (po SizeToContent). Fallback, gdyby layout nie był jeszcze policzony.
        var widthDip = ActualWidth > 0 ? ActualWidth : 408;
        var heightDip = ActualHeight > 0 ? ActualHeight : 178;

        var rect = OverlayPositioner.Place(cursorX, cursorY, widthDip, heightDip, monitor);

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            rect.X, rect.Y, rect.Width, rect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Rozszerzone style „no-activate": okno nie przejmuje aktywacji ani fokusa i nie ląduje w Alt+Tab.
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, exStyle);
    }
}
