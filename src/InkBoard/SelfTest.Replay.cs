using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenarios for recording and replay: "replay" records a short lesson and plays it back;
// a second launch with "replay-read" checks the recording was saved with the page.
public sealed partial class MainWindow
{
    /// <summary>A stroke whose ink points carry timestamps, as pen input does.</summary>
    private static InkStroke TimedStroke(InkDrawingAttributes attributes, Point from, Point to, double durationMs, int pointCount = 60)
    {
        var points = new List<InkPoint>();
        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            var position = new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t + 20 * Math.Sin(t * 12));
            points.Add(new InkPoint(position, 0.3f + 0.5f * t, 0, 0, (ulong)(1_000_000 + t * durationMs * 1000)));
        }

        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(attributes);
        return builder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity);
    }

    private async Task<int> DrawAsync(InkDrawingAttributes attributes, Point from, Point to, double durationMs)
    {
        await Task.Delay((int)durationMs);   // the pen is "down" for this long
        int handle = _document.AddNew(TimedStroke(attributes, from, to, durationMs));
        _undo.Push(new AddStrokesStep([handle]) { Drawn = true });
        return handle;
    }

    private int StrokesOnPage() => _document.Container.GetStrokes().Count;

    internal async Task RunReplayTestAsync(StringBuilder log)
    {
        InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
        pen.Size = new Size(5, 5);
        pen.Color = Microsoft.UI.Colors.Navy;

        // Something that was on the page before recording started.
        _undo.Push(new AddStrokesStep([_document.AddNew(SelfTest.MakeStroke(pen, new Point(150, 80), new Point(500, 80)))]));

        StartRecording();
        int first = await DrawAsync(pen, new Point(150, 150), new Point(700, 200), 1200);

        var text = new PageObject { Kind = PageObjectKind.Text, X = 150, Y = 260 };
        ShowObject(text);
        foreach (int length in Enumerable.Range(1, "Replay me".Length))
        {
            await Task.Delay(100);
            SetText(text, "Replay me"[..length]);
        }

        // Notes to self: must never reach the recording.
        InkDrawingAttributes secret = Presenter.CopyDefaultDrawingAttributes();
        secret.Color = PrivateInk.Color;
        secret.Size = new Size(5, 5);
        _undo.Push(new AddStrokesStep([_document.AddNew(TimedStroke(secret, new Point(150, 420), new Point(700, 420), 800))]) { Drawn = true });
        var secretText = new PageObject { Kind = PageObjectKind.Text, X = 150, Y = 480, Text = "say: next comes the far stroke", Color = PrivateInk.Hex };
        AddObjectWithUndo(secretText);

        await Task.Delay(4000);   // a long pause, to be shortened to 2 s on replay
        await DrawAsync(pen, new Point(200, 2400), new Point(900, 2500), 1500);
        await Task.Delay(300);
        _document.Remove([first]);
        _undo.Push(new RemoveStrokesStep([first]));
        await Task.Delay(300);
        StopRecording();

        log.AppendLine($"recorded {_recording.Events.Count} events: " +
            string.Join(", ", _recording.Events.GroupBy(e => e.Kind).Select(g => $"{g.Key} x{g.Count()}")));
        log.AppendLine($"  recorded strokes {_recordedStrokeCount} (expect 3: pre-existing + two drawn; no private), " +
            $"private in events: {_recording.Events.Any(e => e.Object is not null && PrivateInk.IsPrivate(e.Object))}");
        log.AppendLine($"  drawn stroke has point times: {_recording.Events.First(e => e.Duration > 0).PointTimes?.Count} points, " +
            $"duration {_recording.Events.First(e => e.Duration > 0).Duration:0} ms");
        var timeline = new ReplayTimeline(_recording);
        log.AppendLine($"  recording length {_recording.Length:0} ms, replay length {timeline.Length:0} ms (long pause shortened)");
        int pageStrokes = StrokesOnPage();
        await SaveCurrentPageAsync();

        // ---- Replay at 2.5x ----
        SpeedBox.SelectedIndex = 4;
        await EnterReplayAsync();
        log.AppendLine($"replay start: strokes {StrokesOnPage()} (expect 1, the private note), objects {_objects.Count} (expect 1, private text)");

        var watch = Stopwatch.StartNew();
        SetReplayPlaying(true);
        int maxPartial = 0, partialSamples = 0;
        var seen = new SortedSet<int>();
        while (!_clock.AtEnd && watch.ElapsedMilliseconds < 30000)
        {
            await Task.Delay(30);
            if (_drawing.FirstOrDefault() is { } drawing)
            {
                partialSamples++;
                maxPartial = Math.Max(maxPartial, drawing.Shown);
                seen.Add(drawing.Shown);
            }
        }

        log.AppendLine($"replay finished in {watch.ElapsedMilliseconds} ms real time for {_clock.Length:0} ms at 2.5x (expect about {_clock.Length / 2.5:0})");
        log.AppendLine($"  growing stroke observed {partialSamples} times with {seen.Count} different lengths, up to {maxPartial} of 60 points");
        log.AppendLine($"  end of replay: strokes {StrokesOnPage()} (expect {pageStrokes}), text \"{_objects.FirstOrDefault(o => !PrivateInk.IsPrivate(o))?.Text}\"");
        log.AppendLine($"  camera: vertical offset {CanvasScroller.VerticalOffset:0} (expect well above 1500, following the stroke at y=2400)");

        // ---- Pause ----
        RestartReplay();
        SetReplayPlaying(true);
        await Task.Delay(700);
        SetReplayPlaying(false);
        double pausedAt = _clock.Position;
        int strokesAtPause = StrokesOnPage();
        await Task.Delay(600);
        log.AppendLine($"pause: position {pausedAt:0} -> {_clock.Position:0} after waiting (expect unchanged), strokes {strokesAtPause} -> {StrokesOnPage()}");

        // Leave replay paused part-way through the second stroke for the screenshot.
        SpeedBox.SelectedIndex = 1;
        SetReplayPlaying(true);
        while (_drawing.Count == 0 || _nextItem < 3 || _drawing[0].Item.Start < 3000 || _drawing[0].Shown < 30)
        {
            await Task.Delay(20);
            if (_clock.AtEnd)
            {
                break;
            }
        }

        SetReplayPlaying(false);
        log.AppendLine($"paused mid-stroke at {_clock.Position:0} ms with {(_drawing.Count > 0 ? _drawing[0].Shown : -1)} points shown; speed {_clock.Speed}");
        await Task.Delay(500);
    }

    internal async Task RunReplayReadTestAsync(StringBuilder log)
    {
        log.AppendLine($"loaded recording: {_recording.Events.Count} events, {_recordedStrokeCount} strokes, length {_recording.Length:0} ms, " +
            $"{_strokeKeys.Count} page stroke(s) linked to it");
        int strokes = StrokesOnPage(), objects = _objects.Count;

        SpeedBox.SelectedIndex = 4;
        await EnterReplayAsync();
        SetReplayPlaying(true);
        var watch = Stopwatch.StartNew();
        while (!_clock.AtEnd && watch.ElapsedMilliseconds < 30000)
        {
            await Task.Delay(50);
        }

        log.AppendLine($"replay after restart ends with strokes {StrokesOnPage()} (expect {strokes}), objects {_objects.Count} (expect {objects})");
        await ExitReplayAsync();
        log.AppendLine($"after exit: strokes {StrokesOnPage()} (expect {strokes}), objects {_objects.Count} (expect {objects}), " +
            $"editing enabled {Toolbar.IsEnabled}, bar hidden {ReplayBar.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed}");

        // A second take must not duplicate what the recording already contains.
        int eventsBefore = _recording.Events.Count;
        StartRecording();
        await Task.Delay(200);
        StopRecording();
        log.AppendLine($"second take with no changes added {_recording.Events.Count - eventsBefore} events (expect 0)");
    }
}
