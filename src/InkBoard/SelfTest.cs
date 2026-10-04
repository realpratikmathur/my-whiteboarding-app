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
                await Task.Delay(1500);
                await window.RunSelfTestAsync(log);
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
}

public sealed partial class MainWindow
{
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

        // 8. Leave a selection box and a zoomed view on screen for a screenshot.
        Rect all = container.SelectWithPolyLine(
            [new Point(60, 130), new Point(540, 130), new Point(540, 330), new Point(60, 330), new Point(60, 130)]);
        ShowSelection(all);
        CanvasScroller.ChangeView(120, 180, 3f, disableAnimation: true);
        await Task.Delay(1000);
        log.AppendLine($"zoom factor {CanvasScroller.ZoomFactor}");
    }
}
