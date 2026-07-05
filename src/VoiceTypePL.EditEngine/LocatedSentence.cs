using System.Windows;

namespace VoiceTypePL.EditEngine;

/// <summary>
/// Zdanie zlokalizowane i zaznaczone przez <see cref="UiaSentenceLocator"/>: tekst oraz prostokąty
/// (współrzędne ekranu, px) do narysowania ramki podświetlenia (§5.6, Poziom 1).
/// </summary>
public sealed class LocatedSentence
{
    public LocatedSentence(string text, IReadOnlyList<Rect> rectangles)
    {
        Text = text;
        Rectangles = rectangles;
    }

    /// <summary>Tekst zaznaczonego zdania.</summary>
    public string Text { get; }

    /// <summary>Prostokąty ograniczające zaznaczenie (ekran, px) — z <c>GetBoundingRectangles()</c>.</summary>
    public IReadOnlyList<Rect> Rectangles { get; }
}
