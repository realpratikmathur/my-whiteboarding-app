using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class RecordingTests
{
    private static RecordedEvent Stroke(int key, double end, double duration) =>
        new() { Kind = RecordedEventKind.StrokeAdd, Strokes = [key], Time = end, Duration = duration };

    private static Recording Of(params RecordedEvent[] events) => new() { Events = [.. events], Length = events.Max(e => e.Time) };

    [Fact]
    public void Short_pauses_are_kept_as_recorded()
    {
        var timeline = new ReplayTimeline(Of(Stroke(0, 1000, 800), Stroke(1, 2500, 500)));

        Assert.Equal((200, 1000), (timeline.Items[0].Start, timeline.Items[0].End));
        Assert.Equal((2000, 2500), (timeline.Items[1].Start, timeline.Items[1].End));
        Assert.Equal(2500 + ReplayTimeline.Tail, timeline.Length);
    }

    [Fact]
    public void Long_pauses_are_shortened_to_two_seconds()
    {
        // 30 s of silence between the first stroke ending (1000) and the second starting (31000).
        var timeline = new ReplayTimeline(Of(Stroke(0, 1000, 800), Stroke(1, 31500, 500)));

        Assert.Equal(1000 + ReplayTimeline.MaxGap, timeline.Items[1].Start);
        Assert.Equal(1000 + ReplayTimeline.MaxGap + 500, timeline.Items[1].End);
    }

    [Fact]
    public void A_long_wait_before_the_first_event_is_shortened_too()
    {
        var timeline = new ReplayTimeline(Of(Stroke(0, 60000, 1000)));

        Assert.Equal(ReplayTimeline.MaxGap, timeline.Items[0].Start);
    }

    [Fact]
    public void Shortening_accumulates_and_keeps_stroke_durations()
    {
        var timeline = new ReplayTimeline(Of(Stroke(0, 1000, 1000), Stroke(1, 12000, 1000), Stroke(2, 24000, 1000)));

        Assert.All(timeline.Items, item => Assert.Equal(1000, item.End - item.Start));
        Assert.Equal(3000, timeline.Items[1].Start);
        Assert.Equal(6000, timeline.Items[2].Start);
    }

    [Fact]
    public void Events_inside_a_long_stroke_do_not_count_as_idle_time()
    {
        // A 10 s stroke; an instant event 1 s after it ends must not be treated as an 11 s gap.
        var erase = new RecordedEvent { Kind = RecordedEventKind.StrokeRemove, Strokes = [0], Time = 11000 };
        var timeline = new ReplayTimeline(Of(Stroke(0, 10000, 10000), erase));

        Assert.Equal(11000, timeline.Items[1].Start);
    }

    [Fact]
    public void Clock_runs_at_the_chosen_speed_and_stops_at_the_end()
    {
        var clock = new ReplayClock(length: 1000) { IsPlaying = true, Speed = 2.5 };

        clock.Advance(100);
        Assert.Equal(250, clock.Position);

        clock.Speed = 0.5;
        clock.Advance(100);
        Assert.Equal(300, clock.Position);

        clock.Speed = 2;
        clock.Advance(10_000);
        Assert.Equal(1000, clock.Position);
        Assert.True(clock.AtEnd);
        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void Paused_clock_does_not_move()
    {
        var clock = new ReplayClock(1000) { IsPlaying = true };
        clock.Advance(100);
        clock.IsPlaying = false;
        clock.Advance(500);

        Assert.Equal(100, clock.Position);
    }

    [Fact]
    public void Offered_speeds_are_half_to_two_and_a_half()
    {
        Assert.Equal([0.5, 1, 1.5, 2, 2.5], ReplayClock.Speeds);
    }

    [Fact]
    public void State_at_end_reflects_adds_removes_and_edits()
    {
        var id = Guid.NewGuid();
        var recording = Of(
            Stroke(0, 100, 0),
            Stroke(1, 200, 0),
            new RecordedEvent { Kind = RecordedEventKind.StrokeRemove, Strokes = [0], Time = 300 },
            new RecordedEvent { Kind = RecordedEventKind.ObjectAdd, ObjectId = id, Time = 400, Object = new PageObject { Id = id, Text = "" } },
            new RecordedEvent { Kind = RecordedEventKind.ObjectText, ObjectId = id, Time = 500, Text = "Hi" },
            new RecordedEvent { Kind = RecordedEventKind.ObjectRect, ObjectId = id, Time = 600, X = 5, Y = 6, Width = 7, Height = 8 });

        RecordingState state = RecordingState.AtEnd(recording);

        Assert.Equal([1], state.Strokes);
        PageObject obj = state.Objects[id];
        Assert.Equal("Hi", obj.Text);
        Assert.Equal((5, 6, 7, 8), (obj.X, obj.Y, obj.Width, obj.Height));
    }

    [Fact]
    public void Recording_round_trips_through_json()
    {
        var recording = Of(Stroke(3, 1500, 700));
        recording.Events[0].PointTimes = [0, 350.5f, 700];

        Recording loaded = System.Text.Json.JsonSerializer.Deserialize<Recording>(
            System.Text.Json.JsonSerializer.Serialize(recording))!;

        Assert.Equal(1500, loaded.Length);
        Assert.Equal([3], loaded.Events[0].Strokes);
        Assert.Equal([0, 350.5f, 700], loaded.Events[0].PointTimes);
        Assert.Equal(800, loaded.Events[0].Start);
    }
}

public class CameraFollowerTests
{
    [Fact]
    public void Target_in_the_middle_of_the_screen_does_not_move_the_view()
    {
        var camera = new CameraFollower();

        Assert.Equal((100, 200), camera.Step(100, 200, 1000, 600, targetX: 600, targetY: 450, elapsedMs: 33));
    }

    [Fact]
    public void Target_below_the_band_glides_until_it_is_centred()
    {
        var camera = new CameraFollower();
        double y = 0, previous = 0;
        for (int i = 0; i < 300; i++)
        {
            (_, y) = camera.Step(0, y, 1000, 600, targetX: 500, targetY: 900, elapsedMs: 33);
            Assert.True(y >= previous);
            previous = y;
        }

        // Rest position: the target sits 45% down the screen.
        Assert.Equal(900 - 600 * 0.45, y, 3);
    }

    [Fact]
    public void Glide_is_gradual()
    {
        var camera = new CameraFollower();
        (_, double y) = camera.Step(0, 0, 1000, 600, 500, 900, elapsedMs: 33);

        // Even the faster off-screen glide covers only part of the 630 px distance in one tick.
        Assert.InRange(y, 1, 200);
    }

    [Fact]
    public void A_jump_far_down_the_page_is_reached_within_a_second()
    {
        var camera = new CameraFollower();
        double y = 0;
        for (int i = 0; i < 30; i++)   // 30 ticks of 33 ms
        {
            (_, y) = camera.Step(0, y, 1000, 600, targetX: 500, targetY: 2400, elapsedMs: 33);
        }

        // The target is on screen again, in the upper three quarters.
        Assert.InRange((2400 - y) / 600, 0.2, 0.75);
    }

    [Fact]
    public void View_never_scrolls_above_or_left_of_the_page_origin()
    {
        var camera = new CameraFollower();
        double x = 400, y = 400;
        for (int i = 0; i < 300; i++)
        {
            (x, y) = camera.Step(x, y, 1000, 600, targetX: 10, targetY: 10, elapsedMs: 33);
        }

        Assert.Equal((0, 0), (x, y));
    }

    [Fact]
    public void Writing_down_the_page_scrolls_down_steadily()
    {
        var camera = new CameraFollower();
        double y = 0;
        for (int i = 0; i < 2000; i++)
        {
            double targetY = 100 + i * 2;   // the lesson moves down the page
            (_, y) = camera.Step(0, y, 1000, 600, 500, targetY, 33);
            Assert.InRange((targetY - y) / 600, 0, 0.75);
        }

        Assert.True(y > 3000);
    }
}
