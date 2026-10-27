using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenario "replay-seek": fast-forward and rewind.
// 1. At many moments, the page after a forward seek and after a backward seek must equal the page reached by
//    playing normally (blank and ghost start).
// 2. Seek times on a large lesson (800 strokes, about 11 minutes).
// 3. The slider driven with REAL mouse input (SendInput): dragging the thumb and clicking the track.
public sealed partial class MainWindow
{
    /// <summary>Everything on the page that a replay puts there, in a comparable form.</summary>
    private string PageSignature()
    {
        var strokes = _document.Container.GetStrokes()
            .Select(s => $"{(PrivateInk.IsPrivate(s) ? "P" : "R")}{s.GetInkPoints().Count}@{s.BoundingRect.X:0},{s.BoundingRect.Y:0},{s.BoundingRect.Width:0}")
            .Order();
        var objects = _objects.Select(o => $"{o.Kind}:{o.Text}@{o.X:0},{o.Y:0}").Order();
        return string.Join(";", strokes) + " | " + string.Join(";", objects);
    }

    private Thumb? SliderThumb(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is Thumb { Name: "HorizontalThumb" } thumb)
            {
                return thumb;
            }

            if (SliderThumb(child) is Thumb found)
            {
                return found;
            }
        }

        return null;
    }

    internal async Task RunReplaySeekTestAsync(StringBuilder log)
    {
        PagesToggle.IsChecked = false;

        // ---- A lesson with erases, a move, text and a private note ----
        InkDrawingAttributes note = Presenter.CopyDefaultDrawingAttributes();
        note.Color = PrivateInk.Color;
        _undo.Push(new AddStrokesStep([_document.AddNew(SelfTest.MakeStroke(note, new Point(700, 500), new Point(1000, 500)))]));
        InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
        pen.Size = new Size(5, 5);

        StartRecording();
        int a = await DrawAsync(pen, new Point(100, 100), new Point(600, 110), 900);
        await DrawAsync(pen, new Point(100, 180), new Point(600, 190), 900);
        int moved = await DrawAsync(pen, new Point(100, 260), new Point(400, 270), 700);
        await Task.Delay(300);
        _document.Move([moved], 250, 40);
        _undo.Push(new MoveStrokesStep([moved], 250, 40));
        var text = new PageObject { Kind = PageObjectKind.Text, X = 700, Y = 150, FontSize = 26 };
        ShowObject(text);
        foreach (int length in Enumerable.Range(1, "Seek me".Length))
        {
            await Task.Delay(80);
            SetText(text, "Seek me"[..length]);
        }

        await Task.Delay(300);
        _document.Remove([a]);
        _undo.Push(new RemoveStrokesStep([a]));
        await DrawAsync(pen, new Point(100, 380), new Point(600, 390), 900);
        StopRecording();
        await SaveCurrentPageAsync();

        FollowBox.SelectedIndex = 2;   // keep the view still: the comparison is about the page content
        foreach (int start in new[] { 0, 1 })
        {
            StartBox.SelectedIndex = start;
            await EnterReplayAsync();
            int checks = 0, mismatches = 0;
            double length = _clock.Length;
            foreach (double fraction in new[] { 0.05, 0.2, 0.33, 0.47, 0.6, 0.74, 0.88, 1.0, 0.4, 0.1, 0.8 })
            {
                // Reference: played from the start in normal ticks.
                RestartReplay();
                AdvanceReplayTo(length * fraction);
                double at = _clock.Position;
                string played = PageSignature();

                // Rewind from the end, and fast-forward from the start.
                SeekReplay(length);
                SeekReplay(at);
                string rewound = PageSignature();
                RestartReplay();
                SeekReplay(at);
                string forwarded = PageSignature();

                checks++;
                if (played != rewound || played != forwarded)
                {
                    mismatches++;
                    log.AppendLine($"  MISMATCH at {at:0} ms:\n    played    {played}\n    rewound   {rewound}\n    forwarded {forwarded}");
                }
            }

            log.AppendLine($"{(start == 1 ? "ghost" : "blank")} start: {checks} moments, page after seeking forward and back " +
                $"{(mismatches == 0 ? "identical to normal playback every time" : $"DIFFERENT {mismatches} times")}");
            await ExitReplayAsync();
        }

        // ---- Speed on a long lesson ----
        _recording = new Recording();
        _recordedStrokes = new Windows.UI.Input.Inking.InkStrokeContainer();
        _recordedStrokeCount = 0;
        const int Count = 800;
        for (int i = 0; i < Count; i++)
        {
            double x = 60 + i % 20 * 55, y = 60 + i / 20 * 45;
            InkStroke stroke = TimedStroke(pen, new Point(x, y), new Point(x + 45, y + 10), 600, 40);
            _recordedStrokes.AddStroke(stroke);
            (double duration, var times) = StrokeTiming(stroke);
            _recording.Events.Add(new RecordedEvent
            {
                Kind = RecordedEventKind.StrokeAdd, Strokes = [_recordedStrokeCount++], Time = i * 800 + 600, Duration = duration, PointTimes = times,
            });
        }

        _recording.Length = Count * 800;
        _recordingChanged = true;
        _pageDirty = true;
        await SaveCurrentPageAsync();
        StartBox.SelectedIndex = 1;   // the ghost page is the heavier case
        var watch = Stopwatch.StartNew();
        await EnterReplayAsync();
        log.AppendLine($"long lesson: {Count} strokes, {TimeSpan.FromMilliseconds(_clock.Length):m\\:ss}; opening the replay with the ghost page took {watch.ElapsedMilliseconds} ms");
        await Task.Delay(1000);   // as a person would: the replay has been open a moment before the first seek
        foreach ((string what, double to) in new[] { ("forward to the end", _clock.Length), ("back to the middle", _clock.Length / 2), ("back to 10%", _clock.Length * 0.1), ("forward to 90%", _clock.Length * 0.9), ("small step back (-5 s)", _clock.Length * 0.9 - 5000) })
        {
            watch.Restart();
            SeekReplay(to);
            log.AppendLine($"  seek {what}: {watch.ElapsedMilliseconds} ms; real strokes {RealStrokesOnPage()}, ghosts {_ghostStrokes.Count}");
        }

        await SelfTest.SnapshotAsync("seek-long");
        await ExitReplayAsync();
    }

    /// <summary>Scenario "replay-seek-mouse": the slider with real mouse input during playback.</summary>
    internal async Task RunReplaySeekMouseTestAsync(StringBuilder log)
    {
        GetCursorPos(out POINT cursorBefore);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        PagesToggle.IsChecked = false;

        try
        {
            InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
            pen.Size = new Size(5, 5);
            StartRecording();
            for (int i = 0; i < 6; i++)
            {
                await DrawAsync(pen, new Point(100, 80 + i * 70), new Point(700, 90 + i * 70), 1500);
            }

            StopRecording();
            await SaveCurrentPageAsync();
            FollowBox.SelectedIndex = 0;
            StartBox.SelectedIndex = 0;
            SpeedBox.SelectedIndex = 1;
            await EnterReplayAsync();
            await Task.Delay(300);
            SetReplayPlaying(true);
            await Task.Delay(1000);

            Thumb thumb = SliderThumb(ReplaySlider)!;
            Point thumbCentre = thumb.TransformToVisual(Root).TransformPoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2));
            Point sliderLeft = ReplaySlider.TransformToVisual(Root).TransformPoint(new Point(0, 0));
            double track = ReplaySlider.ActualWidth - thumb.ActualWidth;
            Point AtFraction(double f) => new(sliderLeft.X + thumb.ActualWidth / 2 + track * f, thumbCentre.Y);
            double length = _clock.Length;
            log.AppendLine($"playing at {_clock.Position:0} of {length:0} ms; slider {ReplaySlider.Value:0}");

            // Drag the thumb to 75% and hold it there: playback waits, the page shows 75%.
            double positionDuringHold = -1;
            bool playingDuringHold = true;
            int strokesDuringHold = -1;
            var drag = RealDragHoldAsync(thumbCentre, AtFraction(0.75), holdMs: 900, onHold: () =>
            {
                positionDuringHold = _clock.Position;
                playingDuringHold = _clock.IsPlaying;
                strokesDuringHold = RealStrokesOnPage();
            });
            await drag;
            log.AppendLine($"thumb dragged to 75% and held: position {positionDuringHold:0} (expect about {length * 0.75:0}), " +
                $"playing while held {playingDuringHold} (expect False), strokes {strokesDuringHold} (expect 5: four done, the fifth being drawn)");
            double afterRelease = _clock.Position;
            await Task.Delay(800);
            log.AppendLine($"released: playing {_clock.IsPlaying} (expect True), position {afterRelease:0} -> {_clock.Position:0} (moving on)");

            // Pause, then click the track at 20%: jumps back there and stays paused.
            SetReplayPlaying(false);
            await RealDragAsync(LeftDown, LeftUp, AtFraction(0.2));
            await Task.Delay(500);
            log.AppendLine($"paused, clicked the track at 20%: position {_clock.Position:0} (expect about {length * 0.2:0}), playing {_clock.IsPlaying} (expect False), " +
                $"strokes {RealStrokesOnPage()} (expect 2: one done, one part-drawn)");

            // Arrow keys on the focused slider step 5 seconds.
            double before = _clock.Position;
            ReplaySlider.Focus(FocusState.Programmatic);
            await Task.Delay(200);
            SendKeyInput(1, [new KEYINPUT { Type = 1, VirtualKey = 0x27 }], 40);
            SendKeyInput(1, [new KEYINPUT { Type = 1, VirtualKey = 0x27, Flags = 0x0002 }], 40);
            await Task.Delay(500);
            log.AppendLine($"right arrow on the slider: {before:0} -> {_clock.Position:0} (expect the next 5-second mark, 5000)");
            FrameworkElement exit = ReplayBar.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Last();
            double exitRight = exit.TransformToVisual(Root).TransformPoint(new Point(exit.ActualWidth, 0)).X;
            double timeRight = ReplayTime.TransformToVisual(Root).TransformPoint(new Point(ReplayTime.ActualWidth, 0)).X;
            log.AppendLine($"window content {Root.ActualWidth:0} x {Root.ActualHeight:0} (scale {Root.XamlRoot.RasterizationScale}); Exit replay right edge {exitRight:0}, time right edge {timeRight:0}; time shows \"{ReplayTime.Text}\"");
            await SelfTest.SnapshotAsync("seek-bar");
            await ExitReplayAsync();
        }
        finally
        {
            presenter.IsAlwaysOnTop = false;
            SetCursorPos(cursorBefore.X, cursorBefore.Y);
        }
    }

    /// <summary>Real mouse: press at one point, drag to another, hold still (running a check), release.</summary>
    private async Task RealDragHoldAsync(Point fromWindow, Point toWindow, int holdMs, Action onHold)
    {
        double scale = Root.XamlRoot.RasterizationScale;
        IntPtr main = WinRT.Interop.WindowNative.GetWindowHandle(this);
        POINT Screen(Point p)
        {
            var s = new POINT { X = (int)(p.X * scale), Y = (int)(p.Y * scale) };
            ClientToScreen(main, ref s);
            return s;
        }

        POINT a = Screen(fromWindow), b = Screen(toWindow);
        SendMouse(a.X, a.Y, 0);
        await Task.Delay(150);
        SendMouse(a.X, a.Y, LeftDown);
        await Task.Delay(80);
        for (int i = 1; i <= 15; i++)
        {
            SendMouse(a.X + (b.X - a.X) * i / 15, a.Y + (b.Y - a.Y) * i / 15, 0);
            await Task.Delay(20);
        }

        await Task.Delay(holdMs);
        onHold();
        SendMouse(b.X, b.Y, LeftUp);
        await Task.Delay(200);
    }
}
