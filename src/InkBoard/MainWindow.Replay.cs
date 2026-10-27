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

        // Dragging the slider: pause while the thumb is held, carry on afterwards if it was playing.
        ReplaySlider.ThumbToolTipValueConverter = new ReplayTimeConverter();
        ReplaySlider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => BeginScrub()), true);
        ReplaySlider.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => EndScrub()), true);
        ReplaySlider.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => EndScrub()), true);
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

        // Put the real page back exactly as it was saved (the reload removes the ghosts from the page).
        _ghostStrokes.Clear();
        _ghostTexts.Clear();
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

    /// <summary>Back to the start: only the private notes are on the page (and the ghost page, if chosen).</summary>
    private void RestartReplay(bool resetView = true, double ghostsAt = -1)
    {
        _replayTimer.Stop();
        ClearGhosts();
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
        _camera = new CameraFollower(FollowMode);
        _clock = new ReplayClock(_timeline.Length) { Speed = ReplayClock.Speeds[Math.Max(0, SpeedBox.SelectedIndex)] };
        if (StartMode == ReplayStartMode.Ghost)
        {
            ShowGhostPage(ghostsAt);
        }

        if (resetView && FollowMode != ReplayFollowMode.Off)
        {
            // With the view under manual control it stays where the presenter put it.
            CanvasScroller.ChangeView(0, 0, null, disableAnimation: true);
        }
        UpdateReplayBar();
    }

    // ---- Transport ------------------------------------------------------------------------------

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => SetReplayPlaying(!_clock.IsPlaying);

    private void OnRestartReplayClick(object sender, RoutedEventArgs e) => RestartReplay();

    private ReplayFollowMode FollowMode =>
        FollowBox.SelectedIndex switch
        {
            1 => ReplayFollowMode.EdgeNudge,
            2 => ReplayFollowMode.Off,
            _ => ReplayFollowMode.Centered,
        };

    /// <summary>Can be chosen before pressing Play and changed while playing; remembered for next time.</summary>
    private void OnFollowModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FollowBox.SelectedIndex < 0)
        {
            return;
        }

        _camera = new CameraFollower(FollowMode);
        if (_notebook.ReplayFollow != FollowMode && !string.IsNullOrEmpty(_notebookFolder) && _notebookReady.Task.IsCompleted)
        {
            _notebook.ReplayFollow = FollowMode;
            SaveNotebookIndex();
        }
    }

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
        _updatingSlider = true;
        ReplaySlider.Maximum = Math.Max(1, _clock.Length);
        if (!_scrubbing)
        {
            ReplaySlider.Value = _clock.Position;
        }

        _updatingSlider = false;
        PlayPauseIcon.Glyph = _clock.IsPlaying ? "" : "";
        ToolTipService.SetToolTip(PlayPauseButton, _clock.IsPlaying ? "Pause" : "Play");
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
        ApplyUpTo(_clock.Position);
        FollowWithCamera(elapsedMs);
        if (_clock.AtEnd)
        {
            _replayTimer.Stop();
        }

        UpdateReplayBar();
    }

    /// <summary>Brings the page up to a replay position: every event that has started by then, strokes being
    /// drawn shown as far as they had got.</summary>
    private void ApplyUpTo(double position)
    {
        while (_nextItem < _timeline.Items.Count && _timeline.Items[_nextItem].Start <= position)
        {
            ReplayTimeline.Item next = _timeline.Items[_nextItem++];

            // Finish strokes that were complete before this event, so that an erase or move right after a
            // stroke (within the same tick) finds that stroke on the page instead of missing it.
            foreach (DrawingStroke drawing in _drawing.Where(d => d.Item.End <= next.Start).ToList())
            {
                Draw(drawing, next.Start);
            }

            Begin(next);
        }

        FlushStrokeAdds();   // before strokes still being drawn, so those stay on top as in normal playback
        foreach (DrawingStroke stroke in _drawing.ToList())
        {
            Draw(stroke, position);
        }
    }

    // ---- Seeking --------------------------------------------------------------------------------

    private bool _updatingSlider;
    private bool _scrubbing;
    private bool _resumeAfterScrub;
    private bool _seekQueued;
    private double _pendingSeek;

    /// <summary>
    /// Jumps to a moment of the replay. Forward: everything up to then is applied at once. Backward: the page
    /// is rebuilt from the start (private notes and the ghost page included) up to then. Playing or paused
    /// stays as it was; the view jumps to the writing unless Follow is off.
    /// </summary>
    internal void SeekReplay(double position)
    {
        if (!_replaying)
        {
            return;
        }

        position = Math.Clamp(position, 0, _clock.Length);
        bool playing = _clock.IsPlaying;
        if (position < _clock.Position)
        {
            RewindTo(position);
        }
        else
        {
            _clock.Seek(position);
            _ghostRemovals = [];
            _strokeAdds = [];
            try
            {
                ApplyUpTo(position);
            }
            finally
            {
                FlushStrokeAdds();
                _strokeAdds = null;
                FlushGhostRemovals();
            }
        }

        if (FollowMode != ReplayFollowMode.Off)
        {
            FollowWithCamera(3000);   // one long step: the view arrives at the writing at once
        }

        _clock.IsPlaying = playing && !_clock.AtEnd;
        if (_clock.IsPlaying)
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

    private static bool Overlaps(Rect a, Rect b) =>
        a.X <= b.X + b.Width && b.X <= a.X + a.Width && a.Y <= b.Y + b.Height && b.Y <= a.Y + a.Height;

    /// <summary>What the last rewind changed on the page (for the self-tests).</summary>
    internal string LastRewindChanges { get; private set; } = "";

    /// <summary>True while a rewind builds the page at its target moment off screen (see RewindTo).</summary>
    private bool _offline;

    /// <summary>
    /// Goes back to an earlier moment without the page ever being emptied on screen. The replay is run again
    /// from the start up to that moment "off screen": strokes are collected instead of being put on the page,
    /// text boxes and pictures are only data. Then the page is switched over in one go: the new strokes are
    /// added first and only then the old ones removed, and text boxes and pictures that are still there keep
    /// their place (only what changed is updated). Emptying the page first and rebuilding it made the ink
    /// layer show the empty page for a moment on every step of a drag to the left.
    /// </summary>
    private void RewindTo(double position)
    {
        _replayTimer.Stop();
        var oldFull = new Dictionary<int, InkStroke>(_replayStrokes);
        var oldOffsets = new Dictionary<int, Vector2>(_replayOffsets);
        var oldGhosts = new Dictionary<int, InkStroke>(_ghostStrokes);
        List<InkStroke> oldPartials = _drawing.Where(d => d.Partial is not null).Select(d => d.Partial!).ToList();
        var oldObjects = new Dictionary<Guid, PageObject>(_replayObjects);
        var oldTextGhosts = new Dictionary<Guid, PageObject>(_ghostTexts);

        _offline = true;
        _strokeAdds = [];
        try
        {
            _replayStrokes.Clear();
            _replayOffsets.Clear();
            _replayObjects.Clear();
            _drawing.Clear();
            _ghostStrokes.Clear();
            _ghostTexts.Clear();
            _nextItem = 0;
            _cameraTarget = null;
            _clock = new ReplayClock(_timeline.Length) { Speed = _clock.Speed };
            if (StartMode == ReplayStartMode.Ghost)
            {
                ShowGhostPage(position);
            }

            _clock.Seek(position);
            ApplyUpTo(position);
        }
        finally
        {
            _offline = false;
        }

        // Strokes and ghosts that are on the page already, unchanged, stay where they are; only the rest is
        // swapped. A small step back then removes a few strokes and adds nothing.
        List<InkStroke> collected = _strokeAdds;
        _strokeAdds = null;
        var dropped = new HashSet<InkStroke>(ReferenceEqualityComparer.Instance);   // collected but not needed
        var kept = new HashSet<InkStroke>(ReferenceEqualityComparer.Instance);      // already on the page
        foreach ((int key, InkStroke clone) in _replayStrokes.ToList())
        {
            if (oldFull.TryGetValue(key, out InkStroke? old) && oldOffsets.GetValueOrDefault(key) == _replayOffsets.GetValueOrDefault(key))
            {
                dropped.Add(clone);
                kept.Add(old);
                _replayStrokes[key] = old;
            }
        }

        var reappearing = new List<InkStroke>();
        foreach ((int key, InkStroke ghost) in _ghostStrokes.ToList())
        {
            if (oldGhosts.TryGetValue(key, out InkStroke? old))
            {
                dropped.Add(ghost);
                kept.Add(old);
                _ghostStrokes[key] = old;
            }
            else
            {
                reappearing.Add(ghost);
            }
        }

        List<InkStroke> fresh = collected.Where(stroke => !dropped.Contains(stroke)).ToList();

        // A ghost that comes back is added on top of the page. Ink that it crosses must stay on top of it
        // (magenta over ink would show as a white gap in the video), so such ink is put back on top as well.
        if (reappearing.Count > 0)
        {
            List<Rect> ghostAreas = reappearing.Select(g => g.BoundingRect).ToList();
            foreach ((int key, InkStroke stroke) in _replayStrokes.ToList())
            {
                Rect area = stroke.BoundingRect;
                if (kept.Contains(stroke) && ghostAreas.Any(g => Overlaps(g, area)))
                {
                    InkStroke onTop = stroke.Clone();
                    fresh.Add(onTop);
                    kept.Remove(stroke);
                    _replayStrokes[key] = onTop;
                }
            }
        }

        // Switch the ink over: new first, then old out, so no moment shows an empty page.
        if (fresh.Count > 0)
        {
            _document.Container.AddStrokes(fresh);
        }

        List<InkStroke> outdated = oldFull.Values.Concat(oldGhosts.Values).Concat(oldPartials).Where(old => !kept.Contains(old)).ToList();
        if (outdated.Count > 0)
        {
            foreach (InkStroke old in outdated)
            {
                old.Selected = true;
            }

            _document.Container.DeleteSelected();
        }

        LastRewindChanges = $"{fresh.Count} added, {outdated.Count} removed, {kept.Count} kept";

        // Text boxes and pictures: keep the ones still there and update only what changed.
        foreach ((Guid id, PageObject target) in _replayObjects.ToList())
        {
            if (!oldObjects.Remove(id, out PageObject? shown))
            {
                ShowObject(target);
                continue;
            }

            bool resized = shown.Kind == PageObjectKind.Image && (shown.Width, shown.Height) != (target.Width, target.Height);
            if ((shown.X, shown.Y) != (target.X, target.Y) || resized)
            {
                SetObjectRect(shown, new Rect(target.X, target.Y, Math.Max(target.Width, 1), Math.Max(target.Height, 1)));
            }

            if (shown.Text != target.Text)
            {
                SetText(shown, target.Text);
            }

            if (!shown.HasSameStyleAs(target))
            {
                SetTextStyle(shown, target);
            }

            _replayObjects[id] = shown;
        }

        foreach (PageObject gone in oldObjects.Values)
        {
            HideObject(gone);
        }

        foreach ((Guid id, PageObject ghost) in _ghostTexts.ToList())
        {
            if (oldTextGhosts.Remove(id, out PageObject? shownGhost))
            {
                _ghostTexts[id] = shownGhost;   // a text ghost never changes: keep the one on screen
            }
            else
            {
                ShowTextGhost(ghost);
            }
        }

        foreach (PageObject gone in oldTextGhosts.Values)
        {
            HideObject(gone);
        }
    }

    private void OnReplaySliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || !_replaying)
        {
            return;
        }

        // Seek to the newest position once the UI is free: a fast drag does not queue up a seek per pixel.
        _pendingSeek = e.NewValue;
        if (!_seekQueued)
        {
            _seekQueued = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _seekQueued = false;
                SeekReplay(_pendingSeek);
            });
        }

        ReplayTime.Text = $"{TimeSpan.FromMilliseconds(e.NewValue):m\\:ss} / {TimeSpan.FromMilliseconds(_clock.Length):m\\:ss}";
    }

    private void BeginScrub()
    {
        if (!_replaying || _scrubbing)
        {
            return;
        }

        _scrubbing = true;
        _resumeAfterScrub = _clock.IsPlaying;
        _clock.IsPlaying = false;
        _replayTimer.Stop();
    }

    private void EndScrub()
    {
        if (!_scrubbing)
        {
            return;
        }

        _scrubbing = false;
        SeekReplay(ReplaySlider.Value);
        if (_resumeAfterScrub && !_clock.AtEnd)
        {
            SetReplayPlaying(true);
        }
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
                if (IsSeeThrough(source))
                {
                    RemoveGhost(e.Strokes[0]);
                }

                break;

            case RecordedEventKind.StrokeAdd:
                foreach (int key in e.Strokes.Where(InRecording))
                {
                    RemoveGhost(key);
                    _cameraTarget = Centre(ShowRecordedStroke(key).BoundingRect);
                }

                break;

            case RecordedEventKind.StrokeRemove when _offline:
                foreach (int key in e.Strokes)
                {
                    if (_replayStrokes.Remove(key, out InkStroke? collected))
                    {
                        _strokeAdds!.Remove(collected);
                    }
                }

                break;

            case RecordedEventKind.StrokeMove when _offline:
                var offset = new Vector2((float)e.Dx, (float)e.Dy);
                Rect? movedTo = null;
                foreach (int key in e.Strokes)
                {
                    _replayOffsets[key] = _replayOffsets.GetValueOrDefault(key) + offset;
                    if (_replayStrokes.TryGetValue(key, out InkStroke? collected))
                    {
                        collected.PointTransform *= Matrix3x2.CreateTranslation(offset);
                        Rect bounds = collected.BoundingRect;
                        if (movedTo is Rect union)
                        {
                            bounds.Union(union);
                        }

                        movedTo = bounds;
                    }
                }

                if (movedTo is Rect area)
                {
                    _cameraTarget = Centre(area);
                }

                break;

            case RecordedEventKind.StrokeRemove:
                FlushStrokeAdds();
                SelectReplayStrokes(e.Strokes, selected: true);
                _document.Container.DeleteSelected();
                foreach (int key in e.Strokes)
                {
                    _replayStrokes.Remove(key);
                }

                break;

            case RecordedEventKind.StrokeMove:
                FlushStrokeAdds();
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
                RemoveTextGhost(e.ObjectId);
                PageObject added = e.Object.Clone();
                _replayObjects[e.ObjectId] = added;
                if (!_offline)
                {
                    ShowObject(added);
                }
                _cameraTarget = new Point(added.X, added.Y);
                break;

            case RecordedEventKind.ObjectRemove when _replayObjects.Remove(e.ObjectId, out PageObject? removed):
                HideObject(removed);
                break;

            case RecordedEventKind.ObjectRect when _replayObjects.TryGetValue(e.ObjectId, out PageObject? moved):
                SetObjectRect(moved, new Rect(e.X, e.Y, Math.Max(e.Width, 1), Math.Max(e.Height, 1)));
                _cameraTarget = new Point(e.X, e.Y);
                break;

            case RecordedEventKind.ObjectStyle when e.Object is not null && _replayObjects.TryGetValue(e.ObjectId, out PageObject? styled):
                SetTextStyle(styled, e.Object);
                _cameraTarget = new Point(styled.X, styled.Y);
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
        if (_strokeAdds is not null)
        {
            _strokeAdds.Add(clone);   // seeking: added together (FlushStrokeAdds)
        }
        else
        {
            _document.Container.AddStroke(clone);
        }

        _replayStrokes[key] = clone;
        return clone;
    }

    /// <summary>While seeking, finished strokes are collected and added to the page in one call.</summary>
    private System.Collections.Generic.List<InkStroke>? _strokeAdds;

    /// <summary>Puts collected strokes on the page; needed before anything selects or removes strokes.</summary>
    private void FlushStrokeAdds()
    {
        if (_offline)
        {
            return;   // a rewind puts everything on the page at the end, in one go
        }

        // Ghosts first: deleting is quick on a page that has not just received hundreds of new strokes, slow
        // right after (the delete waits for the ink layer to finish drawing them).
        if (_ghostRemovals is { Count: > 0 })
        {
            foreach (InkStroke ghost in _ghostRemovals)
            {
                ghost.Selected = true;
            }

            _document.Container.DeleteSelected();
            _ghostRemovals.Clear();
        }

        if (_strokeAdds is { Count: > 0 })
        {
            _document.Container.AddStrokes(_strokeAdds);
            _strokeAdds.Clear();
        }
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
            if (_offline)
            {
                _strokeAdds!.Remove(previous);
            }
            else
            {
                previous.Selected = true;
                _document.Container.DeleteSelected();
            }

            drawing.Partial = null;
        }

        if (finished)
        {
            _cameraTarget = PenPosition(drawing, drawing.Points.Count);
            ShowRecordedStroke(drawing.Key);
            RemoveGhost(drawing.Key);
            _drawing.Remove(drawing);
            return;
        }

        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(drawing.Source.DrawingAttributes);
        InkStroke partial = builder.CreateStrokeFromInkPoints(
            drawing.Points.Take(count), PlacedTransform(drawing.Source, drawing.Key));
        if (_offline)
        {
            _strokeAdds!.Add(partial);
        }
        else
        {
            _document.Container.AddStroke(partial);
        }

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
        const double LeadMs = 1300;   // real time
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
