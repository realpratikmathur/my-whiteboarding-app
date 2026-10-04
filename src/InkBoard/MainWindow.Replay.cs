using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using InkPoint = Windows.UI.Input.Inking.InkPoint;
using InkStroke = Windows.UI.Input.Inking.InkStroke;
using InkStrokeBuilder = Windows.UI.Input.Inking.InkStrokeBuilder;

namespace InkBoard;

// Replaying a recorded lesson on the page itself.
//
// Replay starts with everything except the private notes removed and plays the recorded events back in
// time. A stroke that was drawn by hand is redrawn progressively: each tick the platform builds a stroke
// from the first N of its original ink points (InkStrokeBuilder) and it replaces the previous partial
// stroke in the container, so what is on screen is always platform-rendered ink. Leaving replay reloads
// the page from disk.
public sealed partial class MainWindow
{
    private sealed class DrawingStroke
    {
        public required ReplayTimeline.Item Item { get; init; }
        public required int Key { get; init; }
        public required InkStroke Source { get; init; }
        public required IReadOnlyList<InkPoint> Points { get; init; }
        public InkStroke? Partial { get; set; }
        public int Shown { get; set; }
    }

    private readonly Dictionary<int, InkStroke> _replayStrokes = [];     // recording key -> stroke on screen
    private readonly Dictionary<int, Vector2> _replayOffsets = [];       // moves applied to a key so far
    private readonly Dictionary<Guid, PageObject> _replayObjects = [];
    private readonly List<DrawingStroke> _drawing = [];
    private readonly DispatcherTimer _replayTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _replayWatch = new();

    private bool _replaying;
    private ReplayTimeline _timeline = new(new Recording());
    private ReplayClock _clock = new(0);
    private CameraFollower _camera = new();
    private IReadOnlyList<InkStroke> _recordedList = [];
    private int _nextItem;
    private Point? _cameraTarget;
    private (double X, double Y, float Zoom) _viewBeforeReplay;

    private void ConfigureReplay()
    {
        _replayTimer.Tick += (_, _) => OnReplayTick();
        foreach (double speed in ReplayClock.Speeds)
        {
            SpeedBox.Items.Add($"{speed:0.#}×");
        }

        SpeedBox.SelectedIndex = ReplayClock.Speeds.ToList().IndexOf(1);
    }

    // ---- Enter / leave --------------------------------------------------------------------------

    private async void OnReplayClick(object sender, RoutedEventArgs e) => await EnterReplayAsync();

    private async void OnExitReplayClick(object sender, RoutedEventArgs e) => await ExitReplayAsync();

    internal async Task EnterReplayAsync()
    {
        if (_replaying || _recording.IsEmpty)
        {
            return;
        }

        StopRecording();
        Root.Focus(FocusState.Programmatic);
        _saveTimer.Stop();
        await SaveCurrentPageAsync();

        _viewBeforeReplay = (CanvasScroller.HorizontalOffset, CanvasScroller.VerticalOffset, CanvasScroller.ZoomFactor);
        _replaying = true;
        SetEditingEnabled(false);
        ReplayBar.Visibility = Visibility.Visible;
        _recordedList = _recordedStrokes.GetStrokes();
        _timeline = new ReplayTimeline(_recording);
        RestartReplay();
    }

    internal async Task ExitReplayAsync()
    {
        if (!_replaying || _currentPage is not NotebookPage page)
        {
            return;
        }

        _replayTimer.Stop();
        ReplayBar.Visibility = Visibility.Collapsed;

        // Put the real page back exactly as it was saved.
        _currentPage = null;
        _pageDirty = false;
        await OpenPageAsync(page);
        _replaying = false;
        SetEditingEnabled(true);
        CanvasScroller.ChangeView(_viewBeforeReplay.X, _viewBeforeReplay.Y, _viewBeforeReplay.Zoom, disableAnimation: true);
    }

    private void SetEditingEnabled(bool enabled)
    {
        Presenter.IsInputEnabled = enabled;
        Toolbar.IsEnabled = enabled;
        FileButton.IsEnabled = enabled;
        if (!enabled)
        {
            UndoButton.IsEnabled = false;
            RedoButton.IsEnabled = false;
        }

        ConvertButton.IsEnabled = enabled;
        RecordToggle.IsEnabled = enabled;
        PagesPane.IsHitTestVisible = enabled;
        PagesPane.Opacity = enabled ? 1 : 0.5;
        if (!enabled)
        {
            ReplayButton.IsEnabled = false;
        }
        else
        {
            UpdateRecordLabel();
        }
    }

    /// <summary>Back to the start: only the private notes are on the page.</summary>
    private void RestartReplay()
    {
        _replayTimer.Stop();
        ClearSelection();
        SelectObject(null);

        foreach (InkStroke stroke in _document.Container.GetStrokes())
        {
            stroke.Selected = !PrivateInk.IsPrivate(stroke);
        }

        _document.Container.DeleteSelected();
        foreach (PageObject obj in _objects.Where(o => !PrivateInk.IsPrivate(o)).ToList())
        {
            HideObject(obj);
        }

        _replayStrokes.Clear();
        _replayOffsets.Clear();
        _replayObjects.Clear();
        _drawing.Clear();
        _nextItem = 0;
        _cameraTarget = null;
        _camera = new CameraFollower();
        _clock = new ReplayClock(_timeline.Length) { Speed = ReplayClock.Speeds[Math.Max(0, SpeedBox.SelectedIndex)] };
        CanvasScroller.ChangeView(0, 0, null, disableAnimation: true);
        UpdateReplayBar();
    }

    // ---- Transport ------------------------------------------------------------------------------

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => SetReplayPlaying(!_clock.IsPlaying);

    private void OnRestartReplayClick(object sender, RoutedEventArgs e) => RestartReplay();

    private void OnSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedBox.SelectedIndex >= 0)
        {
            _clock.Speed = ReplayClock.Speeds[SpeedBox.SelectedIndex];
        }
    }

    internal void SetReplayPlaying(bool playing)
    {
        if (!_replaying)
        {
            return;
        }

        if (playing && _clock.AtEnd)
        {
            RestartReplay();
        }

        _clock.IsPlaying = playing;
        if (playing)
        {
            _replayWatch.Restart();
            _replayTimer.Start();
        }
        else
        {
            _replayTimer.Stop();
        }

        UpdateReplayBar();
    }

    private void UpdateReplayBar()
    {
        PlayPauseIcon.Glyph = _clock.IsPlaying ? "" : "";
        ToolTipService.SetToolTip(PlayPauseButton, _clock.IsPlaying ? "Pause" : "Play");
        ReplayProgress.Maximum = Math.Max(1, _clock.Length);
        ReplayProgress.Value = _clock.Position;
        ReplayTime.Text = $"{TimeSpan.FromMilliseconds(_clock.Position):m\\:ss} / {TimeSpan.FromMilliseconds(_clock.Length):m\\:ss}";
    }

    private void OnReplayTick()
    {
        double elapsed = _replayWatch.Elapsed.TotalMilliseconds;
        _replayWatch.Restart();
        AdvanceReplay(elapsed);
    }

    /// <summary>Moves playback forward by real time and brings the page and the view up to date.</summary>
    internal void AdvanceReplay(double elapsedMs)
    {
        _clock.Advance(elapsedMs);
        double position = _clock.Position;

        while (_nextItem < _timeline.Items.Count && _timeline.Items[_nextItem].Start <= position)
        {
            Begin(_timeline.Items[_nextItem++]);
        }

        foreach (DrawingStroke stroke in _drawing.ToList())
        {
            Draw(stroke, position);
        }

        FollowWithCamera(elapsedMs);
        if (_clock.AtEnd)
        {
            _replayTimer.Stop();
        }

        UpdateReplayBar();
    }

    // ---- Applying events ------------------------------------------------------------------------

    private void Begin(ReplayTimeline.Item item)
    {
        RecordedEvent e = item.Event;
        switch (e.Kind)
        {
            case RecordedEventKind.StrokeAdd when e.Duration > 0 && e.Strokes.Count == 1 && InRecording(e.Strokes[0]):
                InkStroke source = _recordedList[e.Strokes[0]];
                _drawing.Add(new DrawingStroke
                {
                    Item = item, Key = e.Strokes[0], Source = source, Points = source.GetInkPoints(),
                });
                break;

            case RecordedEventKind.StrokeAdd:
                foreach (int key in e.Strokes.Where(InRecording))
                {
                    _cameraTarget = Centre(ShowRecordedStroke(key).BoundingRect);
                }

                break;

            case RecordedEventKind.StrokeRemove:
                SelectReplayStrokes(e.Strokes, selected: true);
                _document.Container.DeleteSelected();
                foreach (int key in e.Strokes)
                {
                    _replayStrokes.Remove(key);
                }

                break;

            case RecordedEventKind.StrokeMove:
                var delta = new Vector2((float)e.Dx, (float)e.Dy);
                foreach (int key in e.Strokes)
                {
                    _replayOffsets[key] = _replayOffsets.GetValueOrDefault(key) + delta;
                }

                if (SelectReplayStrokes(e.Strokes, selected: true))
                {
                    _cameraTarget = Centre(_document.Container.MoveSelected(new Point(e.Dx, e.Dy)));
                    SelectReplayStrokes(e.Strokes, selected: false);
                }

                break;

            case RecordedEventKind.ObjectAdd when e.Object is not null:
                PageObject added = e.Object.Clone();
                _replayObjects[e.ObjectId] = added;
                ShowObject(added);
                _cameraTarget = new Point(added.X, added.Y);
                break;

            case RecordedEventKind.ObjectRemove when _replayObjects.Remove(e.ObjectId, out PageObject? removed):
                HideObject(removed);
                break;

            case RecordedEventKind.ObjectRect when _replayObjects.TryGetValue(e.ObjectId, out PageObject? moved):
                SetObjectRect(moved, new Rect(e.X, e.Y, Math.Max(e.Width, 1), Math.Max(e.Height, 1)));
                _cameraTarget = new Point(e.X, e.Y);
                break;

            case RecordedEventKind.ObjectText when _replayObjects.TryGetValue(e.ObjectId, out PageObject? typed):
                SetText(typed, e.Text ?? "");
                // Follow the line being typed.
                _cameraTarget = new Point(typed.X + Math.Min(typed.Width, 400), typed.Bottom);
                break;
        }
    }

    private bool InRecording(int key) => key >= 0 && key < _recordedList.Count;

    private static Point Centre(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private Matrix3x2 PlacedTransform(InkStroke source, int key) =>
        source.PointTransform * Matrix3x2.CreateTranslation(_replayOffsets.GetValueOrDefault(key));

    /// <summary>Puts the complete recorded stroke on the page (an exact clone of what was drawn).</summary>
    private InkStroke ShowRecordedStroke(int key)
    {
        InkStroke clone = _recordedList[key].Clone();
        clone.PointTransform = PlacedTransform(clone, key);
        _document.Container.AddStroke(clone);
        _replayStrokes[key] = clone;
        return clone;
    }

    private bool SelectReplayStrokes(IEnumerable<int> keys, bool selected)
    {
        bool any = false;
        foreach (int key in keys)
        {
            if (_replayStrokes.TryGetValue(key, out InkStroke? stroke))
            {
                stroke.Selected = selected;
                any = true;
            }
        }

        return any;
    }

    /// <summary>Shows as much of a hand-drawn stroke as had been drawn at this moment.</summary>
    private void Draw(DrawingStroke drawing, double position)
    {
        RecordedEvent e = drawing.Item.Event;
        double elapsed = position - drawing.Item.Start;
        bool finished = elapsed >= e.Duration;

        int count = finished ? drawing.Points.Count : PointsDrawnBy(e, drawing.Points.Count, elapsed);
        if (count == drawing.Shown && !finished)
        {
            return;
        }

        if (drawing.Partial is InkStroke previous)
        {
            previous.Selected = true;
            _document.Container.DeleteSelected();
            drawing.Partial = null;
        }

        if (finished)
        {
            _cameraTarget = PenPosition(drawing, drawing.Points.Count);
            ShowRecordedStroke(drawing.Key);
            _drawing.Remove(drawing);
            return;
        }

        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(drawing.Source.DrawingAttributes);
        InkStroke partial = builder.CreateStrokeFromInkPoints(
            drawing.Points.Take(count), PlacedTransform(drawing.Source, drawing.Key));
        _document.Container.AddStroke(partial);
        drawing.Partial = partial;
        drawing.Shown = count;
        _cameraTarget = PenPosition(drawing, count);
    }

    private static int PointsDrawnBy(RecordedEvent e, int total, double elapsedMs)
    {
        int count;
        if (e.PointTimes is { Count: > 0 } times && times.Count == total)
        {
            int index = times.BinarySearch((float)elapsedMs);
            count = index >= 0 ? index + 1 : ~index;
        }
        else
        {
            count = (int)Math.Ceiling(total * elapsedMs / e.Duration);
        }

        return Math.Clamp(count, 1, total);
    }

    private Point PenPosition(DrawingStroke drawing, int count)
    {
        Point p = drawing.Points[count - 1].Position;
        Vector2 placed = Vector2.Transform(new Vector2((float)p.X, (float)p.Y), PlacedTransform(drawing.Source, drawing.Key));
        return new Point(placed.X, placed.Y);
    }

    // ---- Camera ---------------------------------------------------------------------------------

    /// <summary>Where the next event will happen, if it is about to: lets the view get there first.</summary>
    private Point? UpcomingTarget()
    {
        const double LeadMs = 900;   // real time
        if (_drawing.Count > 0 || _nextItem >= _timeline.Items.Count)
        {
            return null;
        }

        ReplayTimeline.Item next = _timeline.Items[_nextItem];
        if (next.Start - _clock.Position > LeadMs * _clock.Speed)
        {
            return null;
        }

        RecordedEvent e = next.Event;
        if (e.Kind == RecordedEventKind.StrokeAdd && e.Strokes.Count > 0 && InRecording(e.Strokes[0]))
        {
            InkStroke stroke = _recordedList[e.Strokes[0]];
            Point first = stroke.GetInkPoints()[0].Position;
            Vector2 placed = Vector2.Transform(new Vector2((float)first.X, (float)first.Y), PlacedTransform(stroke, e.Strokes[0]));
            return new Point(placed.X, placed.Y);
        }

        return e.Kind == RecordedEventKind.ObjectAdd && e.Object is not null ? new Point(e.Object.X, e.Object.Y) : null;
    }

    private void FollowWithCamera(double elapsedMs)
    {
        _cameraTarget = UpcomingTarget() ?? _cameraTarget;
        if (_cameraTarget is not Point target)
        {
            return;
        }

        float zoom = CanvasScroller.ZoomFactor;
        (double x, double y) = _camera.Step(
            CanvasScroller.HorizontalOffset / zoom, CanvasScroller.VerticalOffset / zoom,
            CanvasScroller.ViewportWidth / zoom, CanvasScroller.ViewportHeight / zoom,
            target.X, target.Y, elapsedMs);
        if (Math.Abs(x * zoom - CanvasScroller.HorizontalOffset) > 0.5 || Math.Abs(y * zoom - CanvasScroller.VerticalOffset) > 0.5)
        {
            CanvasScroller.ChangeView(x * zoom, y * zoom, null, disableAnimation: true);
        }
    }
}
