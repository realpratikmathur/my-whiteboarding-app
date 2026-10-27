using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenario "replay-ghost": records a short lesson, replays it with "Start: ghost page", and asks
// the test script for screenshots at fixed points of the replay (start, middle, end), plus one from a
// "Start: blank page" replay at the same moment. Applying the OBS filter to the ghost screenshot must give
// the blank-page screenshot.
public sealed partial class MainWindow
{
    /// <summary>Plays the replay forward to a position in fixed 16 ms steps (same result every run).</summary>
    private void AdvanceReplayTo(double position)
    {
        _clock.IsPlaying = true;
        while (_clock.Position < position && !_clock.AtEnd)
        {
            AdvanceReplay(16);
        }

        _clock.IsPlaying = false;
        _replayTimer.Stop();
        UpdateReplayBar();
    }

    private int RealStrokesOnPage() =>
        _document.Container.GetStrokes().Count(s => !PrivateInk.IsPrivate(s));

    private int PrivateNotesOnPage() =>
        _document.Container.GetStrokes().Count(PrivateInk.IsPrivate) - _ghostStrokes.Count;

    private string GhostState() =>
        $"ghost strokes {_ghostStrokes.Count}, ghost texts {_ghostTexts.Count}, real strokes {RealStrokesOnPage()}, " +
        $"private notes {PrivateNotesOnPage()}, text boxes {_objects.Count(o => !PrivateInk.IsPrivate(o))}";

    internal async Task RunReplayGhostTestAsync(StringBuilder log)
    {
        PagesToggle.IsChecked = false;
        await Task.Delay(300);

        // A private note that exists before recording: never recorded, always on the page.
        InkDrawingAttributes note = Presenter.CopyDefaultDrawingAttributes();
        note.Color = PrivateInk.Color;
        note.Size = new Size(3, 3);
        _undo.Push(new AddStrokesStep([_document.AddNew(SelfTest.MakeStroke(note, new Point(700, 470), new Point(1000, 470)))]));

        InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
        pen.Size = new Size(5, 5);
        pen.Color = Microsoft.UI.Colors.Navy;
        InkDrawingAttributes highlighter = new()
        {
            DrawAsHighlighter = true, PenTip = PenTipShape.Rectangle, Size = new Size(6, 22),
            Color = Microsoft.UI.Colors.Yellow, IgnorePressure = true,
        };
        InkDrawingAttributes pencil = InkDrawingAttributes.CreateForPencil();
        pencil.Size = new Size(8, 8);

        StartRecording();
        await DrawAsync(pen, new Point(120, 150), new Point(620, 160), 1200);
        await DrawAsync(pen, new Point(120, 230), new Point(620, 240), 1200);
        int moved = await DrawAsync(pen, new Point(120, 310), new Point(420, 320), 800);
        _document.Move([moved], 300, 0);
        _undo.Push(new MoveStrokesStep([moved], 300, 0));
        int erased = await DrawAsync(pen, new Point(700, 150), new Point(1000, 160), 600);
        _document.Remove([erased]);
        _undo.Push(new RemoveStrokesStep([erased]));
        await DrawAsync(highlighter, new Point(120, 155), new Point(620, 160), 900);
        await DrawAsync(pencil, new Point(120, 400), new Point(620, 410), 900);

        // A stroke written later that crosses both lines: its ghost lies under them once they are replayed.
        InkDrawingAttributes cross = Presenter.CopyDefaultDrawingAttributes();
        cross.Size = new Size(5, 5);
        cross.Color = Microsoft.UI.Colors.DarkRed;
        _undo.Push(new AddStrokesStep([_document.AddNew(SelfTest.MakeStroke(cross, new Point(560, 110), new Point(575, 290)))]) { Drawn = true });
        await Task.Delay(600);

        var text = new PageObject { Kind = PageObjectKind.Text, X = 700, Y = 220, FontSize = 28 };
        ShowObject(text);
        foreach (int length in Enumerable.Range(1, "Ghost text".Length))
        {
            await Task.Delay(80);
            SetText(text, "Ghost text"[..length]);
        }

        StopRecording();
        await SaveCurrentPageAsync();
        int pageStrokes = StrokesOnPage(), pageObjects = _objects.Count;
        log.AppendLine($"page before replay: {pageStrokes} strokes (incl. 1 private note), {pageObjects} text box");

        // ---- Ghost replay ----
        FollowBox.SelectedIndex = 1;    // edge nudge: the view stays still for a lesson that fits the screen
        StartBox.SelectedIndex = 1;
        await EnterReplayAsync();
        await Task.Delay(500);
        log.AppendLine($"ghost start: {GhostState()} (expect 5 ghost strokes - not the highlighter -, 1 ghost text, 0 real, 1 note, 0 boxes)");
        log.AppendLine($"    all ghosts private colour: {_ghostStrokes.Values.All(PrivateInk.IsPrivate)}; " +
            $"moved stroke ghost right edge {_ghostStrokes.Values.Max(s => s.BoundingRect.Right):0} (expect about 720); " +
            $"ghost widths {string.Join(",", _ghostStrokes.Values.Select(s => s.DrawingAttributes.Size.Width).Distinct())}");
        Point origin = CanvasScroller.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        double scale = Root.XamlRoot.RasterizationScale;
        log.AppendLine($"page area in client pixels: {(int)(origin.X * scale)} {(int)(origin.Y * scale)} " +
            $"{(int)(CanvasScroller.ViewportWidth * scale)} {(int)(CanvasScroller.ViewportHeight * scale)}");
        await SelfTest.SnapshotAsync("ghost-start");

        // Half-way through the second line.
        ReplayTimeline.Item second = _timeline.Items.Where(i => i.Event.Duration > 0).ElementAt(1);
        double middle = (second.Start + second.End) / 2;
        AdvanceReplayTo(middle);
        await Task.Delay(500);
        log.AppendLine($"ghost middle ({middle:0} ms): {GhostState()}; drawing {_drawing.Count} stroke with {(_drawing.Count > 0 ? _drawing[0].Shown : 0)} points");
        log.AppendLine($"    the line being drawn still has its ghost under it: {_ghostStrokes.ContainsKey(_drawing.FirstOrDefault()?.Key ?? -1)}");
        await SelfTest.SnapshotAsync("ghost-middle");

        AdvanceReplayTo(_clock.Length);
        await Task.Delay(500);
        log.AppendLine($"ghost end: {GhostState()} (expect 0 ghosts, {pageStrokes - 1} real, 1 note, 1 box)");
        await SelfTest.SnapshotAsync("ghost-end");

        // Rewind from the end to the middle: ghosts come back (the crossing stroke's among them) and must end up
        // under the ink that has already replayed, exactly as when playing from the start.
        SeekReplay(middle);
        await Task.Delay(500);
        log.AppendLine($"rewound to the middle: {GhostState()}; rewind changed {LastRewindChanges}");
        await SelfTest.SnapshotAsync("ghost-rewound-middle");

        // ---- The same moment with a blank-page replay ----
        StartBox.SelectedIndex = 0;     // starts the replay over
        await Task.Delay(300);
        log.AppendLine($"blank start: {GhostState()} (expect no ghosts, 0 real, 1 note)");
        AdvanceReplayTo(middle);
        await Task.Delay(500);
        await SelfTest.SnapshotAsync("blank-middle");

        // Changing the option back restarts with ghosts again.
        StartBox.SelectedIndex = 1;
        await Task.Delay(300);
        log.AppendLine($"switched back to ghost: {GhostState()}");

        await ExitReplayAsync();
        log.AppendLine($"after exit: {StrokesOnPage()} strokes (expect {pageStrokes}), {_objects.Count} text box (expect {pageObjects}), " +
            $"ghosts left {_ghostStrokes.Count + _ghostTexts.Count}; remembered start mode {_notebook.ReplayStart}");
    }
}
