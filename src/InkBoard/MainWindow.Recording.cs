using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Streams;
using InkPersistenceFormat = Windows.UI.Input.Inking.InkPersistenceFormat;
using InkPoint = Windows.UI.Input.Inking.InkPoint;
using InkStroke = Windows.UI.Input.Inking.InkStroke;
using OsInkStrokeContainer = Windows.UI.Input.Inking.InkStrokeContainer;

namespace InkBoard;

// Recording a lesson. While Record is on, everything that changes on the page is logged with its time:
// strokes (from the undo stack, which already sees every stroke change), text boxes and pictures (from
// the object layer). Private-notes ink and text are never logged.
//
// Per page:  recording.json  the event timeline (all takes appended)
//            recording.isf   a copy of every recorded stroke, in key order
public sealed partial class MainWindow
{
    private readonly Dictionary<int, int> _strokeKeys = [];   // stroke handle -> key in the recording
    private readonly Stopwatch _takeClock = new();
    private readonly DispatcherTimer _recordTicker = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private Recording _recording = new();
    private OsInkStrokeContainer _recordedStrokes = new();
    private int _recordedStrokeCount;
    private bool _recordingChanged;
    private bool _isRecording;
    private double _takeStart;

    /// <summary>Current position on the recording's timeline, in milliseconds.</summary>
    private double RecordingNow => _takeStart + _takeClock.Elapsed.TotalMilliseconds;

    private void ConfigureRecording()
    {
        _undo.Applied += OnStepApplied;
        _recordTicker.Tick += (_, _) => UpdateRecordLabel();
        UpdateRecordLabel();
    }

    // ---- Start / stop ---------------------------------------------------------------------------

    private void OnRecordToggled(object sender, RoutedEventArgs e)
    {
        if (RecordToggle.IsChecked == true)
        {
            StartRecording();
        }
        else
        {
            StopRecording();
        }
    }

    internal void StartRecording()
    {
        if (_isRecording || _replaying)
        {
            return;
        }

        _takeStart = _recording.Length;
        _takeClock.Restart();
        _isRecording = true;
        SyncRecordingWithPage();
        _recordTicker.Start();
        RecordToggle.IsChecked = true;
        UpdateRecordLabel();
    }

    internal void StopRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        Root.Focus(FocusState.Programmatic);   // commits a text box that is being typed in
        _recording.Length = RecordingNow;
        _takeClock.Stop();
        _isRecording = false;
        _recordingChanged = true;
        _recordTicker.Stop();
        RecordToggle.IsChecked = false;
        UpdateRecordLabel();
        MarkPageDirty();
    }

    private void UpdateRecordLabel()
    {
        TimeSpan length = TimeSpan.FromMilliseconds(_isRecording ? RecordingNow : _recording.Length);
        RecordLabel.Text = _isRecording ? $"Stop {length:m\\:ss}" : "Record";
        ReplayButton.IsEnabled = !_isRecording && !_recording.IsEmpty;
        ToolTipService.SetToolTip(ReplayButton, _recording.IsEmpty
            ? "Nothing recorded on this page yet"
            : $"Replay this page's recording ({length:m\\:ss})");
    }

    private async void OnClearRecordingClick(object sender, RoutedEventArgs e)
    {
        if (_recording.IsEmpty || _replaying)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Clear this page's recording?",
            Content = "The page itself stays as it is. The recorded lesson for this page is deleted and cannot be brought back.",
            PrimaryButtonText = "Clear recording",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            StopRecording();
            ResetRecording();
            _recordingChanged = true;
            MarkPageDirty();
        }
    }

    private void ResetRecording()
    {
        _recording = new Recording();
        _recordedStrokes = new OsInkStrokeContainer();
        _recordedStrokeCount = 0;
        _strokeKeys.Clear();
        UpdateRecordLabel();
    }

    // ---- Logging --------------------------------------------------------------------------------

    private RecordedEvent Log(RecordedEvent recorded)
    {
        recorded.Time = RecordingNow;
        _recording.Events.Add(recorded);
        _recordingChanged = true;
        return recorded;
    }

    /// <summary>The recording key for a stroke, copying the stroke into the recording on first use.</summary>
    private int KeyFor(int handle)
    {
        if (!_strokeKeys.TryGetValue(handle, out int key))
        {
            _recordedStrokes.AddStroke(_document.StrokeFor(handle).Clone());
            key = _recordedStrokeCount++;
            _strokeKeys[handle] = key;
        }

        return key;
    }

    private void OnStepApplied(IUndoStep step, bool undone)
    {
        if (!_isRecording)
        {
            return;
        }

        switch (step)
        {
            case AddStrokesStep add when undone:
                LogStrokesRemoved(add.StrokeIds);
                break;
            case AddStrokesStep add:
                LogStrokesAdded(add.StrokeIds, add.Drawn);
                break;
            case RemoveStrokesStep remove when undone:
                LogStrokesAdded(remove.StrokeIds, drawn: false);
                break;
            case RemoveStrokesStep remove:
                LogStrokesRemoved(remove.StrokeIds);
                break;
            case MoveStrokesStep move:
                List<int> moved = RecordedKeys(move.StrokeIds);
                if (moved.Count > 0)
                {
                    int sign = undone ? -1 : 1;
                    Log(new RecordedEvent
                    {
                        Kind = RecordedEventKind.StrokeMove, Strokes = moved, Dx = sign * move.Dx, Dy = sign * move.Dy,
                    });
                }

                break;
            case ReplaceStrokesStep replace:
                LogStrokesRemoved(undone ? replace.AddedIds : replace.RemovedIds);
                LogStrokesAdded(undone ? replace.RemovedIds : replace.AddedIds, drawn: false);
                break;
            case CompanionStep companion:
                // The object half is logged by the object layer itself.
                OnStepApplied(companion.Inner, undone);
                break;
        }
    }

    private List<int> RecordedKeys(IEnumerable<int> handles) =>
        handles.Where(_strokeKeys.ContainsKey).Select(h => _strokeKeys[h]).ToList();

    private void LogStrokesAdded(IReadOnlyList<int> handles, bool drawn)
    {
        List<int> visible = handles.Where(h => !PrivateInk.IsPrivate(_document.StrokeFor(h))).ToList();
        if (visible.Count == 0)
        {
            return;
        }

        if (!drawn)
        {
            Log(new RecordedEvent { Kind = RecordedEventKind.StrokeAdd, Strokes = visible.Select(KeyFor).ToList() });
            return;
        }

        foreach (int handle in visible)
        {
            (double duration, List<float>? pointTimes) = StrokeTiming(_document.StrokeFor(handle));
            Log(new RecordedEvent
            {
                Kind = RecordedEventKind.StrokeAdd, Strokes = [KeyFor(handle)], Duration = duration, PointTimes = pointTimes,
            });
        }
    }

    private void LogStrokesRemoved(IReadOnlyList<int> handles)
    {
        List<int> keys = RecordedKeys(handles);
        if (keys.Count > 0)
        {
            Log(new RecordedEvent { Kind = RecordedEventKind.StrokeRemove, Strokes = keys });
        }
    }

    /// <summary>How long the pen took, and when each point was made, from the stroke's own input data.</summary>
    private static (double DurationMs, List<float>? PointTimes) StrokeTiming(InkStroke stroke)
    {
        IReadOnlyList<InkPoint> points = stroke.GetInkPoints();
        double duration = stroke.StrokeDuration?.TotalMilliseconds ?? 0;
        ulong first = points[0].Timestamp, last = points[^1].Timestamp;
        if (last > first)
        {
            // InkPoint timestamps are in microseconds.
            List<float> times = points.Select(p => (float)((p.Timestamp - first) / 1000.0)).ToList();
            return (Math.Max(duration, times[^1]), times);
        }

        // No timing data (strokes built in code): assume pen sampling of about 120 points a second.
        return (duration > 0 ? duration : points.Count * 8.0, null);
    }

    private void LogObject(RecordedEventKind kind, PageObject obj)
    {
        if (!_isRecording || _loadingPage || PrivateInk.IsPrivate(obj))
        {
            return;
        }

        if (kind == RecordedEventKind.ObjectRect
            && _recording.Events.LastOrDefault() is { Kind: RecordedEventKind.ObjectRect } last
            && last.ObjectId == obj.Id && RecordingNow - last.Time < 40)
        {
            // A drag produces a burst of moves; 25 a second is plenty for replay.
            (last.X, last.Y, last.Width, last.Height) = (obj.X, obj.Y, obj.Width, obj.Height);
            return;
        }

        Log(new RecordedEvent
        {
            Kind = kind,
            ObjectId = obj.Id,
            Object = kind is RecordedEventKind.ObjectAdd or RecordedEventKind.ObjectStyle ? obj.Clone() : null,
            X = obj.X, Y = obj.Y, Width = obj.Width, Height = obj.Height,
            Text = kind == RecordedEventKind.ObjectText ? obj.Text : null,
        });
    }

    partial void OnObjectTextTyped(PageObject obj) => LogObject(RecordedEventKind.ObjectText, obj);

    /// <summary>
    /// At the start of a take, brings the recording in line with the page: whatever was added, removed or
    /// edited while not recording appears (or disappears) at once at this point of the lesson.
    /// </summary>
    private void SyncRecordingWithPage()
    {
        RecordingState state = RecordingState.AtEnd(_recording);

        IReadOnlyList<int> handles = _document.Track(_document.Container.GetStrokes().Where(s => !PrivateInk.IsPrivate(s)));
        List<int> unknown = handles
            .Where(h => !_strokeKeys.TryGetValue(h, out int key) || !state.Strokes.Contains(key))
            .Select(KeyFor).ToList();
        if (unknown.Count > 0)
        {
            Log(new RecordedEvent { Kind = RecordedEventKind.StrokeAdd, Strokes = unknown });
        }

        HashSet<int> onPage = handles.Select(h => _strokeKeys[h]).ToHashSet();
        List<int> gone = state.Strokes.Where(key => !onPage.Contains(key)).ToList();
        if (gone.Count > 0)
        {
            Log(new RecordedEvent { Kind = RecordedEventKind.StrokeRemove, Strokes = gone });
        }

        List<PageObject> objects = _objects.Where(o => !PrivateInk.IsPrivate(o)).ToList();
        foreach (PageObject obj in objects)
        {
            if (!state.Objects.TryGetValue(obj.Id, out PageObject? known))
            {
                LogObject(RecordedEventKind.ObjectAdd, obj);
                continue;
            }

            if (!known.HasSameStyleAs(obj))
            {
                LogObject(RecordedEventKind.ObjectStyle, obj);
            }

            if (known.Text != obj.Text)
            {
                LogObject(RecordedEventKind.ObjectText, obj);
            }

            if ((known.X, known.Y) != (obj.X, obj.Y)
                || (obj.Kind == PageObjectKind.Image && (known.Width, known.Height) != (obj.Width, obj.Height)))
            {
                LogObject(RecordedEventKind.ObjectRect, obj);
            }
        }

        foreach (Guid id in state.Objects.Keys.Where(id => objects.All(o => o.Id != id)).ToList())
        {
            Log(new RecordedEvent { Kind = RecordedEventKind.ObjectRemove, ObjectId = id });
        }
    }

    // ---- Per-page files -------------------------------------------------------------------------

    private partial async Task SaveRecordingAsync(string pageFolder)
    {
        if (!_recordingChanged)
        {
            return;
        }

        _recordingChanged = false;
        string jsonPath = Path.Combine(pageFolder, "recording.json"), inkPath = Path.Combine(pageFolder, "recording.isf");
        if (_recording.IsEmpty)
        {
            File.Delete(jsonPath);
            File.Delete(inkPath);
            return;
        }

        if (_isRecording)
        {
            _recording.Length = RecordingNow;
        }

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(_recording));
        if (_recordedStrokeCount > 0)
        {
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(
                inkPath, FileAccessMode.ReadWrite, StorageOpenOptions.None, FileOpenDisposition.CreateAlways);
            using IOutputStream output = stream.GetOutputStreamAt(0);
            await _recordedStrokes.SaveAsync(output, InkPersistenceFormat.Isf);
            await output.FlushAsync();
        }
    }

    private partial async Task LoadRecordingAsync(string pageFolder)
    {
        string jsonPath = Path.Combine(pageFolder, "recording.json"), inkPath = Path.Combine(pageFolder, "recording.isf");
        _recording = File.Exists(jsonPath)
            ? JsonSerializer.Deserialize<Recording>(await File.ReadAllTextAsync(jsonPath)) ?? new Recording()
            : new Recording();
        _recordedStrokes = new OsInkStrokeContainer();
        if (File.Exists(inkPath))
        {
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(inkPath, FileAccessMode.Read);
            using IInputStream input = stream.GetInputStreamAt(0);
            await _recordedStrokes.LoadAsync(input);
        }

        _recordedStrokeCount = _recordedStrokes.GetStrokes().Count;
        _recordingChanged = false;
        UpdateRecordLabel();
    }
}
