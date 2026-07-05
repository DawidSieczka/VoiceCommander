using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VoiceTypePL.App.Tray;

/// <summary>
/// Generuje ikonę zasobnika w kodzie: rysuje mikrofon przez WPF (<see cref="RenderTargetBitmap"/>),
/// a następnie pakuje piksele we własnoręcznie zbudowany plik .ico (32bpp BGRA + maska AND).
/// H.NotifyIcon oddaje strumień źródła wprost do <c>System.Drawing.Icon</c>, który akceptuje wyłącznie
/// format .ico — dlatego PNG/RenderTargetBitmap nie wystarczą. Format .ico budujemy ręcznie, żeby nie
/// dokładać do naszego kodu zależności od System.Drawing.Common. Kolor akcentu odróżnia stan
/// (nasłuch = zielony, pauza = szary).
/// </summary>
internal static class TrayIconFactory
{
    private const int Size = 32;

    private static readonly Brush Background = CreateFrozen(Color.FromRgb(0x1E, 0x1E, 0x1E));

    /// <summary>
    /// Rysuje ikonę w podanym kolorze, zapisuje jako .ico do <paramref name="icoPath"/> i zwraca
    /// zamrożony <see cref="BitmapImage"/> wskazujący na ten plik (UriSource).
    /// </summary>
    public static ImageSource CreateAndSave(Color accent, string icoPath)
    {
        var bgra = RenderBgraPixels(accent);
        WriteIcoFile(icoPath, bgra);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;   // wczytaj od razu, nie blokuj pliku
        image.UriSource = new Uri(icoPath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Renderuje ikonę i zwraca piksele w formacie BGRA (prosta alfa), rzędy od góry.</summary>
    private static byte[] RenderBgraPixels(Color accent)
    {
        var mic = new SolidColorBrush(accent);
        var pen = new Pen(mic, 2);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Tło — zaokrąglony kwadrat.
            dc.DrawRoundedRectangle(Background, null, new Rect(0, 0, Size, Size), 6, 6);

            // Korpus mikrofonu.
            dc.DrawRoundedRectangle(mic, null, new Rect(12, 5, 8, 13), 4, 4);

            // Pałąk (łuk pod korpusem).
            var figure = new PathFigure { StartPoint = new Point(9, 15) };
            figure.Segments.Add(new ArcSegment(
                new Point(23, 15),
                new Size(7, 7),
                rotationAngle: 0,
                isLargeArc: false,
                SweepDirection.Clockwise,
                isStroked: true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            dc.DrawGeometry(null, pen, geometry);

            // Nóżka i podstawka.
            dc.DrawLine(pen, new Point(16, 22), new Point(16, 26));
            dc.DrawLine(pen, new Point(11, 26), new Point(21, 26));
        }

        var rtb = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        // Konwersja z premultiplied (Pbgra32) na prostą alfę (Bgra32) — .ico oczekuje prostej alfy.
        var converted = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        var stride = Size * 4;
        var pixels = new byte[stride * Size];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    /// <summary>Buduje jednoobrazkowy plik .ico (32bpp) z pikseli BGRA (rzędy od góry).</summary>
    private static void WriteIcoFile(string icoPath, byte[] topDownBgra)
    {
        var stride = Size * 4;
        var xorSize = stride * Size;                       // 32bpp kolor
        var andStride = ((Size + 31) / 32) * 4;            // maska 1bpp, rząd wyrównany do 4 bajtów
        var andSize = andStride * Size;
        var dibSize = 40 + xorSize + andSize;              // BITMAPINFOHEADER + XOR + AND
        const int imageOffset = 6 + 16;                    // ICONDIR + jeden ICONDIRENTRY

        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
        using var stream = new FileStream(icoPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var w = new BinaryWriter(stream);

        // ICONDIR
        w.Write((ushort)0);        // reserved
        w.Write((ushort)1);        // type = icon
        w.Write((ushort)1);        // liczba obrazków

        // ICONDIRENTRY
        w.Write((byte)Size);       // szerokość
        w.Write((byte)Size);       // wysokość
        w.Write((byte)0);          // paleta (0 = brak)
        w.Write((byte)0);          // reserved
        w.Write((ushort)1);        // planes
        w.Write((ushort)32);       // bpp
        w.Write((uint)dibSize);    // rozmiar danych obrazka
        w.Write((uint)imageOffset);

        // BITMAPINFOHEADER (wysokość podwojona: XOR + AND)
        w.Write(40u);              // biSize
        w.Write(Size);             // biWidth
        w.Write(Size * 2);         // biHeight (XOR + AND)
        w.Write((ushort)1);        // biPlanes
        w.Write((ushort)32);       // biBitCount
        w.Write(0u);               // biCompression = BI_RGB
        w.Write((uint)xorSize);    // biSizeImage
        w.Write(0);                // biXPelsPerMeter
        w.Write(0);                // biYPelsPerMeter
        w.Write(0u);               // biClrUsed
        w.Write(0u);               // biClrImportant

        // XOR — piksele BGRA od dołu do góry (DIB jest bottom-up).
        for (var y = Size - 1; y >= 0; y--)
        {
            w.Write(topDownBgra, y * stride, stride);
        }

        // AND — maska przezroczystości; przy 32bpp z alfą zerujemy (przezroczystość niesie kanał alfa).
        for (var i = 0; i < andSize; i++)
        {
            w.Write((byte)0);
        }
    }

    private static Brush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
