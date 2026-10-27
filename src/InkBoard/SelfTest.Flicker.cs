using System;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using InkBoard.Core;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenario "rewind-flicker" [blank|ghost]: a full screen of ink, then twenty small rewinds in a row
// (from the end back to the middle), as when dragging the replay slider to the left. Meanwhile the test script
// films the window as fast as it can and counts the dark ink pixels in every frame: a flash of an emptied
// page shows up as a frame with far less ink than where the rewind ends.
public sealed partial class MainWindow
{
    internal async Task RunRewindFlickerTestAsync(StringBuilder log, string mode)
    {
        PagesToggle.IsChecked = false;
        InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
        pen.Size = new Size(5, 5);

        // 300 strokes filling the visible page, each drawn in 600 ms, one every 800 ms.
        _recording = new Recording();
        _recordedStrokes = new InkStrokeContainer();
        _recordedStrokeCount = 0;
        for (int i = 0; i < 300; i++)
        {
            double x = 40 + i % 20 * 65, y = 40 + i / 20 * 38;
            InkStroke stroke = TimedStroke(pen, new Point(x, y), new Point(x + 55, y + 8), 600, 30);
            _recordedStrokes.AddStroke(stroke);
            (double duration, var times) = StrokeTiming(stroke);
            _recording.Events.Add(new RecordedEvent
            {
                Kind = RecordedEventKind.StrokeAdd, Strokes = [_recordedStrokeCount++], Time = i * 800 + 600, Duration = duration, PointTimes = times,
            });
        }

        _recording.Length = 300 * 800;
        _recordingChanged = true;
        _pageDirty = true;
        await SaveCurrentPageAsync();

        FollowBox.SelectedIndex = 2;   // the view stays still, so every frame shows the same part of the page
        StartBox.SelectedIndex = mode == "ghost" ? 1 : 0;
        await EnterReplayAsync();
        CanvasScroller.ChangeView(0, 0, 1f, disableAnimation: true);
        SeekReplay(_clock.Length);
        await Task.Delay(1500);

        string folder = Path.GetDirectoryName(SelfTest.LogPath)!;
        string start = Path.Combine(folder, "capture-start.txt"), stop = Path.Combine(folder, "capture-stop.txt");
        File.WriteAllText(start, "");
        await Task.Delay(400);   // the script is filming

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 1; i <= 20; i++)
        {
            SeekReplay(_clock.Length * (1 - i * 0.025));
            await Task.Delay(60);
        }

        long rewindMs = watch.ElapsedMilliseconds;
        await Task.Delay(600);
        File.WriteAllText(stop, "");
        for (int i = 0; i < 100 && File.Exists(stop); i++)
        {
            await Task.Delay(100);   // the script deletes it when it has finished
        }

        log.AppendLine($"{mode} start: 20 rewinds from the end to the middle in {rewindMs} ms (20 x 60 ms of it waiting between steps); last step: {LastRewindChanges}; real strokes now {RealStrokesOnPage()} (expect 151, the last one part-drawn), ghosts {_ghostStrokes.Count}");
        await ExitReplayAsync();
    }
}
