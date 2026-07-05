using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Microsoft.Extensions.Logging;

namespace VoiceTypePL.EditEngine;

/// <summary>
/// Poziom 1 edycji (§5.6): przez UI Automation znajduje element tekstowy pod kursorem, wyznacza zakres
/// pod punktem (<c>RangeFromPoint</c>), zawęża go do jednego zdania (przez <see cref="SentenceBoundary"/>
/// zastosowane do akapitu/linii — spójnie i niezależnie od tego, czy aplikacja wspiera jednostkę
/// „Sentence"), zaznacza (<c>Select()</c>) i zwraca tekst + prostokąty do podświetlenia.
/// </summary>
public sealed class UiaSentenceLocator
{
    private const int MaxAncestorHops = 8;

    private readonly ILogger<UiaSentenceLocator>? _logger;

    public UiaSentenceLocator(ILogger<UiaSentenceLocator>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Lokalizuje i zaznacza zdanie pod punktem ekranu (px). Zwraca <c>null</c>, gdy pod kursorem nie
    /// ma edytowalnego tekstu dostępnego przez UIA (wtedy Etap 6 spróbuje Poziomów 2/3).
    /// </summary>
    public LocatedSentence? LocateAndSelect(int screenX, int screenY)
    {
        var point = new Point(screenX, screenY);

        AutomationElement? element;
        try
        {
            element = AutomationElement.FromPoint(point);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "UIA ElementFromPoint nie powiodło się.");
            return null;
        }

        var textPattern = FindTextPattern(element);
        if (textPattern is null)
        {
            _logger?.LogInformation("Pod kursorem brak elementu z TextPattern — Poziom 1 niedostępny.");
            return null;
        }

        try
        {
            var pointRange = textPattern.RangeFromPoint(point);
            var sentence = BuildSentenceRange(pointRange);
            if (sentence is null)
            {
                return null;
            }

            sentence.Select();
            var text = (sentence.GetText(-1) ?? string.Empty).Trim();
            var rectangles = ToRects(sentence.GetBoundingRectangles());
            _logger?.LogInformation("Zaznaczono zdanie do edycji: \"{Text}\".", text);
            return new LocatedSentence(text, rectangles);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Nie udało się wyznaczyć zakresu zdania przez UIA.");
            return null;
        }
    }

    /// <summary>Zawęża zakres punktu do zdania: kontener (akapit/linia) + <see cref="SentenceBoundary"/>.</summary>
    private TextPatternRange? BuildSentenceRange(TextPatternRange pointRange)
    {
        var container = ExpandToContainer(pointRange);
        if (container is null)
        {
            return null;
        }

        var containerText = container.GetText(-1) ?? string.Empty;
        if (containerText.Length == 0)
        {
            return null;
        }

        // Offset punktu w kontenerze = długość tekstu od początku kontenera do punktu.
        var head = container.Clone();
        head.MoveEndpointByRange(TextPatternRangeEndpoint.End, pointRange, TextPatternRangeEndpoint.Start);
        var offset = (head.GetText(-1) ?? string.Empty).Length;

        var (start, length) = SentenceBoundary.Expand(containerText, offset);
        if (length == 0)
        {
            return null;
        }

        var sentence = container.Clone();
        sentence.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, start);
        var tail = containerText.Length - (start + length);
        if (tail > 0)
        {
            sentence.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, -tail);
        }

        return sentence;
    }

    private static TextPatternRange? ExpandToContainer(TextPatternRange pointRange)
    {
        foreach (var unit in new[] { TextUnit.Paragraph, TextUnit.Line })
        {
            try
            {
                var range = pointRange.Clone();
                range.ExpandToEnclosingUnit(unit);
                if (!string.IsNullOrEmpty(range.GetText(1)))
                {
                    return range;
                }
            }
            catch
            {
                // jednostka nieobsługiwana — spróbuj kolejnej
            }
        }

        return null;
    }

    private TextPattern? FindTextPattern(AutomationElement? element)
    {
        var walker = TreeWalker.ControlViewWalker;
        var current = element;
        for (var hop = 0; hop < MaxAncestorHops && current is not null; hop++)
        {
            try
            {
                if (current.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                {
                    return (TextPattern)pattern;
                }

                current = walker.GetParent(current);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static IReadOnlyList<Rect> ToRects(Rect[] raw)
    {
        var rects = new List<Rect>();
        foreach (var rect in raw)
        {
            if (rect.Width > 0 && rect.Height > 0)
            {
                rects.Add(rect);
            }
        }

        return rects;
    }
}
