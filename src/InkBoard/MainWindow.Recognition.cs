using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.Input.Inking.Analysis;
using InkDrawingAttributes = Windows.UI.Input.Inking.InkDrawingAttributes;
using InkDrawingAttributesKind = Windows.UI.Input.Inking.InkDrawingAttributesKind;
using InkPoint = Windows.UI.Input.Inking.InkPoint;
using InkStroke = Windows.UI.Input.Inking.InkStroke;
using InkStrokeBuilder = Windows.UI.Input.Inking.InkStrokeBuilder;

namespace InkBoard;

// Recognition: InkAnalyzer-driven "Ink to text" and "Ink to shape" on the lasso selection.
public sealed partial class MainWindow
{
    private async void OnInkToTextClick(object sender, RoutedEventArgs e) =>
        await RunRecognitionAsync(InkToTextAsync);

    private async void OnInkToShapeClick(object sender, RoutedEventArgs e) =>
        await RunRecognitionAsync(InkToShapeAsync);

    private async Task RunRecognitionAsync(Func<IReadOnlyList<int>, Task<string>> convert)
    {
        try
        {
            StatusText.Text = await convert(_document.SelectedHandles());
        }
        catch (Exception e)
        {
            StatusText.Text = "Recognition failed: " + e.Message;
        }
    }

    private static async Task<InkAnalyzer> AnalyzeAsync(IReadOnlyList<InkStroke> strokes, InkAnalysisStrokeKind kind)
    {
        var analyzer = new InkAnalyzer();
        analyzer.AddDataForStrokes(strokes);
        foreach (InkStroke stroke in strokes)
        {
            analyzer.SetStrokeDataKind(stroke.Id, kind);
        }

        await analyzer.AnalyzeAsync();
        return analyzer;
    }

    // ---- Ink to text ----------------------------------------------------------------------------

    internal async Task<string> InkToTextAsync(IReadOnlyList<int> handles)
    {
        if (handles.Count == 0)
        {
            return "Select ink with the lasso first";
        }

        List<InkStroke> strokes = handles.Select(_document.StrokeFor).ToList();
        InkAnalyzer analyzer = await AnalyzeAsync(strokes, InkAnalysisStrokeKind.Writing);
        List<string> lines = analyzer.AnalysisRoot.FindNodes(InkAnalysisNodeKind.Line)
            .OfType<InkAnalysisLine>()
            .Select(line => line.RecognizedText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        if (lines.Count == 0)
        {
            return "No handwriting recognised";
        }

        Rect bounds = strokes.Select(s => s.BoundingRect).Aggregate((a, b) =>
        {
            a.Union(b);
            return a;
        });
        var text = new PageObject
        {
            Kind = PageObjectKind.Text,
            Text = string.Join("\r", lines),
            FontSize = Math.Max(12, bounds.Height / lines.Count * 0.7),
            X = bounds.X,
            Y = bounds.Y,
        };

        ClearSelection();
        _document.Remove(handles);
        ShowObject(text);
        _undo.Push(new CompanionStep(
            new RemoveStrokesStep(handles),
            AfterUndo: () => HideObject(text),
            AfterRedo: () => ShowObject(text)));

        return "Recognised: " + string.Join(" / ", lines);
    }

    // ---- Ink to shape ---------------------------------------------------------------------------

    internal async Task<string> InkToShapeAsync(IReadOnlyList<int> handles)
    {
        if (handles.Count == 0)
        {
            return "Select ink with the lasso first";
        }

        List<InkStroke> strokes = handles.Select(_document.StrokeFor).ToList();
        Dictionary<uint, InkStroke> strokeById = strokes.ToDictionary(s => s.Id);
        InkAnalyzer analyzer = await AnalyzeAsync(strokes, InkAnalysisStrokeKind.Drawing);

        var replaced = new HashSet<uint>();
        var cleanStrokes = new List<InkStroke>();
        var found = new List<string>();

        foreach (InkAnalysisInkDrawing drawing in
                 analyzer.AnalysisRoot.FindNodes(InkAnalysisNodeKind.InkDrawing).OfType<InkAnalysisInkDrawing>())
        {
            List<uint> ids = drawing.GetStrokeIds().Where(strokeById.ContainsKey).ToList();
            List<Point2> corners = drawing.Points.Select(p => new Point2(p.X, p.Y)).ToList();
            if (ids.Count == 0 || corners.Count != 4)
            {
                continue;
            }

            switch (drawing.DrawingKind)
            {
                case InkAnalysisDrawingKind.Rectangle or InkAnalysisDrawingKind.Square:
                    cleanStrokes.Add(BuildCleanStroke(ShapeGeometry.ClosedPolygon(corners), strokeById[ids[0]], sharpCorners: true));
                    break;
                case InkAnalysisDrawingKind.Ellipse or InkAnalysisDrawingKind.Circle:
                    cleanStrokes.Add(BuildCleanStroke(
                        ShapeGeometry.Ellipse(corners[0], corners[1], corners[2], corners[3]), strokeById[ids[0]], sharpCorners: false));
                    break;
                default:
                    continue;
            }

            replaced.UnionWith(ids);
            found.Add(drawing.DrawingKind.ToString().ToLowerInvariant());
        }

        // InkAnalyzer has no "line" kind; a single stroke that hugs its own chord is treated as one.
        foreach (InkStroke stroke in strokes.Where(s => !replaced.Contains(s.Id)))
        {
            List<Point2> positions = InkDocument.CanvasPositions(stroke);
            if (ShapeGeometry.IsStraightLine(positions))
            {
                cleanStrokes.Add(BuildCleanStroke(ShapeGeometry.Line(positions[0], positions[^1]), stroke, sharpCorners: true));
                replaced.Add(stroke.Id);
                found.Add("line");
            }
        }

        if (cleanStrokes.Count == 0)
        {
            return "No rectangle, ellipse or line recognised";
        }

        ClearSelection();
        IReadOnlyList<int> removed = _document.Track(replaced.Select(id => strokeById[id]));
        _document.Remove(removed);
        List<int> added = cleanStrokes.Select(_document.AddNew).ToList();
        _undo.Push(new ReplaceStrokesStep(removed, added));

        return "Converted: " + string.Join(", ", found);
    }

    /// <summary>
    /// Has the platform build a stroke along an ideal outline, with the hand-drawn stroke's own drawing
    /// attributes (colour, size, tip, pencil/highlighter) and uniform pressure.
    /// </summary>
    private static InkStroke BuildCleanStroke(IReadOnlyList<Point2> outline, InkStroke source, bool sharpCorners)
    {
        InkDrawingAttributes attributes = source.DrawingAttributes;
        if (sharpCorners && attributes.Kind == InkDrawingAttributesKind.Default)
        {
            // Curve fitting would round a rectangle's corners.
            attributes.FitToCurve = false;
        }

        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(attributes);
        return builder.CreateStrokeFromInkPoints(
            outline.Select(p => new InkPoint(new Point(p.X, p.Y), 0.5f)), Matrix3x2.Identity);
    }
}
