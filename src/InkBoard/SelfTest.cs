using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

/// <summary>
/// Developer aid: <c>InkBoard.exe --selftest &lt;log file&gt;</c> exercises the stroke APIs that do not need
/// a pen (programmatic strokes, selection, move, partial erase, undo/redo) and writes what the platform
/// returned. It cannot generate pen input; see MANUAL_TESTS.md for that.
/// </summary>
public static class SelfTest
{
    internal static string LogPath { get; private set; } = "";

    private static readonly Lazy<string?> NotebookOverride = new(() =>
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--selftest");
        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        // Self-tests never touch the real notebook: they use a scratch one next to the log, wiped on every
        // run except the "-read" scenarios, which check what an earlier run saved.
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[index + 1]))!, "selftest-notebook");
        bool keep = args.Length > index + 2 && args[index + 2].EndsWith("-read", StringComparison.Ordinal);
        if (!keep && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        return folder;
    });

    /// <summary>Notebook folder to use instead of DocumentsInkBoard while self-testing; null otherwise.</summary>
    internal static string? NotebookFolderOverride => NotebookOverride.Value;

    public static void RunIfRequested(MainWindow window)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--selftest");
        if (index < 0 || index + 1 >= args.Length)
        {
            return;
        }

        string logPath = LogPath = Path.GetFullPath(args[index + 1]);
        ((FrameworkElement)window.Content).Loaded += async (_, _) =>
        {
            var log = new StringBuilder();
            try
            {
                await window.NotebookReady;
                await Task.Delay(800);
                // --selftest <log> [scenario] [scenario argument]
                string scenario = args.Length > index + 2 ? args[index + 2] : "core";
                string argument = args.Length > index + 3 ? args[index + 3] : "";
                switch (scenario)
                {
                    case "flyout":
                        // Opens a tool's configuration flyout so a screenshot can show whether it renders.
                        window.OpenToolFlyout(argument, log);
                        await Task.Delay(1500);
                        break;
                    case "bigpage":
                        await window.RunBigPageTestAsync(log);
                        break;
                    case "notebook-write":
                        await window.RunNotebookWriteTestAsync(log);
                        break;
                    case "pagemenu":
                        await window.RunPageMenuTestAsync(log);
                        break;
                    case "notebook-read":
                        await window.RunNotebookReadTestAsync(log);
                        break;
                    case "obs-chart":
                        await window.RunObsChartAsync(log, argument);
                        break;
                    case "obs-final":
                        await window.RunObsFinalAsync(log);
                        break;
                    case "replay":
                        await window.RunReplayTestAsync(log);
                        break;
                    case "replay-read":
                        await window.RunReplayReadTestAsync(log);
                        break;
                    case "objects":
                        await window.RunObjectsTestAsync(log);
                        break;
                    default:
                        await window.RunSelfTestAsync(log);
                        break;
                }

                log.AppendLine("DONE");
            }
            catch (Exception e)
            {
                log.AppendLine("FAILED: " + e);
            }

            File.WriteAllText(logPath, log.ToString());
        };
    }

    public static InkStroke MakeStroke(InkDrawingAttributes attributes, Point from, Point to, int pointCount = 41)
    {
        var points = new List<InkPoint>();
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            var position = new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
            points.Add(new InkPoint(position, 0.2f + 0.7f * t));
        }

        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(attributes);
        return builder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity);
    }

    public static InkStroke MakePath(InkDrawingAttributes attributes, IEnumerable<Point> positions)
    {
        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(attributes);
        return builder.CreateStrokeFromInkPoints(positions.Select(p => new InkPoint(p, 0.5f)), Matrix3x2.Identity);
    }

    /// <summary>A tiny valid PDF (US Letter, one line of Helvetica per page) for testing PDF import.</summary>
    public static string MinimalPdf(IReadOnlyList<string> pageTexts)
    {
        var objects = new List<string>();
        int n = pageTexts.Count;
        // 1 catalog, 2 pages, 3 font, then (page, content) pairs.
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Count {n} /Kids [{string.Join(" ", Enumerable.Range(0, n).Select(i => $"{4 + 2 * i} 0 R"))}] >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        for (int i = 0; i < n; i++)
        {
            string content = $"BT /F1 36 Tf 72 700 Td ({pageTexts[i]}) Tj ET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + 2 * i} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToString();
    }

    public static IEnumerable<Point> Between(Point from, Point to, int count) =>
        Enumerable.Range(0, count + 1).Select(i => new Point(
            from.X + (to.X - from.X) * i / count, from.Y + (to.Y - from.Y) * i / count));
}

public sealed partial class MainWindow
{
    internal void OpenToolFlyout(string tool, StringBuilder log)
    {
        Microsoft.UI.Xaml.Controls.InkToolbarToolButton button = tool switch
        {
            "pencil" => PencilButton,
            "highlighter" => HighlighterButton,
            "calligraphy" => CalligraphyButton,
            _ => BallpointButton,
        };
        Toolbar.ActiveTool = button;
        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button);
        var expand = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.ExpandCollapse)
            as Microsoft.UI.Xaml.Automation.Provider.IExpandCollapseProvider;
        log.AppendLine($"flyout for {tool}: expand pattern {(expand is null ? "missing" : "found")}");
        expand?.Expand();
        log.AppendLine($"  state {expand?.ExpandCollapseState}");
    }

    internal async Task RunSelfTestAsync(StringBuilder log)
    {
        int collected = 0, erased = 0;
        Presenter.StrokesCollected += (_, _) => collected++;
        Presenter.StrokesErased += (_, _) => erased++;
        var container = _document.Container;
        int Count() => container.GetStrokes().Count;

        log.AppendLine($"UI thread id {Environment.CurrentManagedThreadId}");

        // 1. Programmatic strokes.
        InkDrawingAttributes ballpoint = Presenter.CopyDefaultDrawingAttributes();
        ballpoint.Size = new Size(6, 6);
        InkDrawingAttributes pencil = InkDrawingAttributes.CreateForPencil();
        pencil.Size = new Size(8, 8);
        InkDrawingAttributes highlighter = new()
        {
            DrawAsHighlighter = true, PenTip = PenTipShape.Rectangle, Size = new Size(8, 24),
            Color = Microsoft.UI.Colors.Yellow,
        };

        int a = _document.AddNew(SelfTest.MakeStroke(ballpoint, new Point(100, 100), new Point(500, 100)));
        int b = _document.AddNew(SelfTest.MakeStroke(pencil, new Point(100, 160), new Point(500, 260)));
        int c = _document.AddNew(SelfTest.MakeStroke(highlighter, new Point(100, 300), new Point(500, 300)));
        _undo.Push(new AddStrokesStep([a, b, c]));
        log.AppendLine($"added 3 strokes; container has {Count()}; ids {string.Join(",", container.GetStrokes().Select(s => s.Id))}");
        log.AppendLine($"stroke A bounds {_document.StrokeFor(a).BoundingRect}");

        // 2. Lasso selection around A only.
        Rect selected = container.SelectWithPolyLine(
            [new Point(80, 80), new Point(520, 80), new Point(520, 120), new Point(80, 120), new Point(80, 80)]);
        log.AppendLine($"SelectWithPolyLine -> {selected}; selected handles [{string.Join(",", _document.SelectedHandles())}] (expect [{a}])");

        // 3. Move.
        Rect moved = container.MoveSelected(new Point(0, 500));
        _undo.Push(new MoveStrokesStep(_document.SelectedHandles(), 0, 500));
        log.AppendLine($"MoveSelected(0,500) -> {moved}; PointTransform {_document.StrokeFor(a).PointTransform}");
        ShowSelection(moved);
        await Task.Delay(300);

        // 4. Partial erase through the moved ballpoint stroke and the pencil stroke.
        ClearSelection();
        int before = Count();
        var session = new PartialEraseSession(_document);
        session.EraseAlong(new Point(300, 560), new Point(300, 640), PointEraserRadius);   // A is now at y=600
        session.EraseAlong(new Point(300, 150), new Point(300, 280), PointEraserRadius);   // pencil
        IUndoStep? step = session.Finish();
        if (step is not null)
        {
            _undo.Push(step);
        }

        // The second sweep also clips the highlighter (tall tip), so all three strokes are split in two.
        log.AppendLine($"partial erase: {before} -> {Count()} strokes (expect {before * 2})");
        foreach (InkStroke s in container.GetStrokes())
        {
            InkDrawingAttributes d = s.DrawingAttributes;
            log.AppendLine(
                $"  stroke {s.Id}: kind {d.Kind}, size {d.Size}, highlighter {d.DrawAsHighlighter}, " +
                $"points {s.GetInkPoints().Count}, pressure {s.GetInkPoints()[0].Pressure:0.00}..{s.GetInkPoints()[^1].Pressure:0.00}, bounds {s.BoundingRect}");
        }

        // 5. Delete one live stroke through the selection, then bring it back as a clone.
        int live = _document.Track(container.GetStrokes()[0]);
        int beforeDelete = Count();
        _document.Remove([live]);
        log.AppendLine($"delete via selection: {beforeDelete} -> {Count()} (expect {beforeDelete - 1})");
        _document.Add([live]);
        log.AppendLine($"re-added clone: {Count()} (expect {beforeDelete}); new platform id {_document.StrokeFor(live).Id}");

        // 6. Undo everything, then redo everything.
        var counts = new List<int>();
        while (_undo.CanUndo)
        {
            Undo();
            counts.Add(Count());
        }

        log.AppendLine($"undo x{counts.Count} stroke counts: {string.Join(",", counts)} (expect 3,3,0)");
        counts.Clear();
        while (_undo.CanRedo)
        {
            Redo();
            counts.Add(Count());
        }

        log.AppendLine($"redo stroke counts: {string.Join(",", counts)} (expect 3,3,6)");
        log.AppendLine($"events raised by programmatic changes: StrokesCollected {collected}, StrokesErased {erased}");

        // 7. ISF round trip and PNG export, next to the log file.
        string folder = Path.GetDirectoryName(SelfTest.LogPath)!;
        string isf = Path.Combine(folder, "selftest.isf"), png = Path.Combine(folder, "selftest-export.png");
        string Describe() => string.Join(" | ", container.GetStrokes().Select(s =>
            // ISF stores pressure as an integer level, so compare to one decimal.
            $"{s.DrawingAttributes.Kind}/{s.GetInkPoints().Count}pts/{s.GetInkPoints()[^1].Pressure:0.0}/{s.BoundingRect.X:0},{s.BoundingRect.Y:0}"));
        string beforeSave = Describe();
        await SaveIsfAsync(isf);
        log.AppendLine($"saved {new FileInfo(isf).Length} bytes of ISF");
        await LoadIsfAsync(isf);
        log.AppendLine($"ISF round trip identical: {beforeSave == Describe()} ({Count()} strokes)");
        if (beforeSave != Describe())
        {
            log.AppendLine("  before: " + beforeSave);
            log.AppendLine("  after:  " + Describe());
        }

        Undo();
        log.AppendLine($"undo of open restores previous strokes: {Count()}");
        try
        {
            await ExportPngAsync(png);
            log.AppendLine($"exported PNG, {new FileInfo(png).Length} bytes");
        }
        catch (Exception e)
        {
            log.AppendLine("PNG export FAILED: " + e);
        }

        // 8. Ink to shape on hand-drawn-looking strokes (deterministic wobble), then ink to text on the result.
        var wobbly = Presenter.CopyDefaultDrawingAttributes();
        wobbly.Size = new Size(4, 4);
        Point[] corners = [new(120, 380), new(320, 384), new(316, 500), new(124, 496), new(121, 383)];
        int rectangle = _document.AddNew(SelfTest.MakePath(wobbly, corners.SelectMany((p, i) =>
            i == 0 ? [] : SelfTest.Between(corners[i - 1], p, 20))));
        int ellipse = _document.AddNew(SelfTest.MakePath(wobbly, Enumerable.Range(0, 61).Select(i =>
        {
            double t = 2 * Math.PI * i / 58;
            return new Point(480 + (90 + 3 * Math.Sin(5 * t)) * Math.Cos(t), 440 + (50 + 2 * Math.Cos(7 * t)) * Math.Sin(t));
        })));
        int line = _document.AddNew(SelfTest.MakePath(wobbly, Enumerable.Range(0, 40).Select(i =>
            new Point(620 + i * 7, 400 + i * 1.5 + 2 * Math.Sin(i)))));
        int beforeShapes = Count();
        log.AppendLine("ink to shape: " + await InkToShapeAsync([rectangle, ellipse, line]));
        log.AppendLine($"  strokes {beforeShapes} -> {Count()}; FitToCurve of results: " +
            string.Join(",", container.GetStrokes().TakeLast(3).Select(s => s.DrawingAttributes.FitToCurve)));
        Undo();
        log.AppendLine($"  undo -> {Count()} strokes; redo ->");
        Redo();
        log.AppendLine("ink to text on shapes (expect no handwriting): " +
            await InkToTextAsync(_document.Track(container.GetStrokes().TakeLast(3))));
        Undo();
        log.AppendLine($"  undo of ink to text -> {Count()} strokes, {_objects.Count} text boxes");

        // 9. Leave a selection box on screen for a screenshot.
        Rect all = container.SelectWithPolyLine(
            [new Point(60, 130), new Point(540, 130), new Point(540, 330), new Point(60, 330), new Point(60, 130)]);
        ShowSelection(all);
        float zoom = Environment.GetCommandLineArgs().Contains("--zoom3") ? 3f : 1f;
        CanvasScroller.ChangeView(zoom == 1f ? 0 : 120, zoom == 1f ? 0 : 180, zoom, disableAnimation: true);
        await Task.Delay(1000);
        log.AppendLine($"zoom factor {CanvasScroller.ZoomFactor}");
    }
}
