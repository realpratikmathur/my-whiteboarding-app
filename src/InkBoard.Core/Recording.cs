using System.Text.Json.Serialization;

namespace InkBoard.Core;

public enum RecordedEventKind
{
    StrokeAdd,
    StrokeRemove,
    StrokeMove,
    ObjectAdd,
    ObjectRemove,
    ObjectRect,
    ObjectText,
}

/// <summary>
/// One thing that happened on the page while recording. Strokes are referred to by their key in the
/// recording's own stroke file, so replay never depends on what is still on the page.
/// </summary>
public sealed class RecordedEvent
{
    public RecordedEventKind Kind { get; set; }

    /// <summary>Milliseconds on the recording's timeline at which the event was complete.</summary>
    public double Time { get; set; }

    /// <summary>How long a hand-drawn stroke took, in milliseconds. Zero for everything that appears at once.</summary>
    public double Duration { get; set; }

    public List<int> Strokes { get; set; } = [];

    /// <summary>For a drawn stroke: when each ink point was made, in milliseconds from the stroke's start.</summary>
    public List<float>? PointTimes { get; set; }

    public double Dx { get; set; }
    public double Dy { get; set; }

    public Guid ObjectId { get; set; }

    /// <summary>Full copy of the object for <see cref="RecordedEventKind.ObjectAdd"/>.</summary>
    public PageObject? Object { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? Text { get; set; }

    [JsonIgnore]
    public double Start => Time - Duration;
}

/// <summary>A page's lesson: every take appended on one timeline.</summary>
public sealed class Recording
{
    public List<RecordedEvent> Events { get; set; } = [];

    /// <summary>Total recorded time in milliseconds.</summary>
    public double Length { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Events.Count == 0;
}

/// <summary>What is on the page according to the recording, after playing some or all of its events.</summary>
public sealed class RecordingState
{
    public HashSet<int> Strokes { get; } = [];
    public Dictionary<Guid, PageObject> Objects { get; } = [];

    public static RecordingState AtEnd(Recording recording)
    {
        var state = new RecordingState();
        foreach (RecordedEvent e in recording.Events)
        {
            state.Apply(e);
        }

        return state;
    }

    public void Apply(RecordedEvent e)
    {
        switch (e.Kind)
        {
            case RecordedEventKind.StrokeAdd:
                Strokes.UnionWith(e.Strokes);
                break;
            case RecordedEventKind.StrokeRemove:
                Strokes.ExceptWith(e.Strokes);
                break;
            case RecordedEventKind.ObjectAdd when e.Object is not null:
                Objects[e.ObjectId] = e.Object.Clone();
                break;
            case RecordedEventKind.ObjectRemove:
                Objects.Remove(e.ObjectId);
                break;
            case RecordedEventKind.ObjectRect when Objects.TryGetValue(e.ObjectId, out PageObject? moved):
                (moved.X, moved.Y, moved.Width, moved.Height) = (e.X, e.Y, e.Width, e.Height);
                break;
            case RecordedEventKind.ObjectText when Objects.TryGetValue(e.ObjectId, out PageObject? typed):
                typed.Text = e.Text ?? "";
                break;
        }
    }
}

/// <summary>
/// The recording as it will be played: events in start order with idle gaps longer than
/// <see cref="MaxGap"/> shortened to that length.
/// </summary>
public sealed class ReplayTimeline
{
    public const double MaxGap = 2000;

    /// <summary>Quiet time kept after the last event.</summary>
    public const double Tail = 500;

    public readonly record struct Item(RecordedEvent Event, double Start, double End);

    public ReplayTimeline(Recording recording, double maxGap = MaxGap)
    {
        var items = new List<Item>();
        double removed = 0, busyUntil = 0;
        foreach (RecordedEvent e in recording.Events.OrderBy(e => e.Start).ThenBy(e => e.Time))
        {
            double idle = e.Start - busyUntil;
            if (idle > maxGap)
            {
                removed += idle - maxGap;
            }

            items.Add(new Item(e, e.Start - removed, e.Time - removed));
            busyUntil = Math.Max(busyUntil, e.Time);
        }

        Items = items;
        Length = items.Count == 0 ? 0 : items.Max(i => i.End) + Tail;
    }

    public IReadOnlyList<Item> Items { get; }

    /// <summary>Playback length in milliseconds at 1x.</summary>
    public double Length { get; }
}

/// <summary>Playback position with speed and pause.</summary>
public sealed class ReplayClock(double length)
{
    public static readonly IReadOnlyList<double> Speeds = [0.5, 1, 1.5, 2, 2.5];

    public double Length { get; } = length;
    public double Position { get; private set; }
    public double Speed { get; set; } = 1;
    public bool IsPlaying { get; set; }

    public bool AtEnd => Position >= Length;

    /// <summary>Moves the position forward by real elapsed time; stops at the end.</summary>
    public void Advance(double elapsedMs)
    {
        if (!IsPlaying)
        {
            return;
        }

        Position = Math.Min(Length, Position + elapsedMs * Speed);
        if (AtEnd)
        {
            IsPlaying = false;
        }
    }

    public void Restart()
    {
        Position = 0;
        IsPlaying = false;
    }
}
