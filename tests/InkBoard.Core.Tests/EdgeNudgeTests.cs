using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class EdgeNudgeTests
{
    private const double ViewWidth = 1000, ViewHeight = 600, Tick = 16;

    [Fact]
    public void Writing_anywhere_but_the_edges_leaves_the_view_alone()
    {
        var camera = new CameraFollower(ReplayFollowMode.EdgeNudge);

        // Centred mode would already be gliding for the last three of these.
        foreach ((double x, double y) in new[] { (500.0, 300.0), (120, 80), (850, 300), (500, 480), (880, 500) })
        {
            Assert.Equal((0, 0), camera.Step(0, 0, ViewWidth, ViewHeight, x, y, Tick));
        }
    }

    [Fact]
    public void Reaching_the_last_line_nudges_down_by_a_small_part_of_the_screen()
    {
        var camera = new CameraFollower(ReplayFollowMode.EdgeNudge);
        double y = 0;
        for (int i = 0; i < 600; i++)
        {
            (_, y) = camera.Step(0, y, ViewWidth, ViewHeight, 400, 540, Tick);   // 90% down the screen
        }

        // The line being written ends up at 72% of the height: a scroll of 108 px, not half a screen.
        Assert.Equal(540 - ViewHeight * 0.72, y, 3);
        Assert.InRange(y, 60, 150);
    }

    [Fact]
    public void Veering_to_the_right_edge_nudges_right()
    {
        var camera = new CameraFollower(ReplayFollowMode.EdgeNudge);
        double x = 0;
        for (int i = 0; i < 600; i++)
        {
            (x, _) = camera.Step(x, 0, ViewWidth, ViewHeight, 950, 300, Tick);
        }

        Assert.Equal(950 - ViewWidth * 0.78, x, 3);
    }

    [Fact]
    public void The_nudge_is_soft_never_a_jump()
    {
        var camera = new CameraFollower(ReplayFollowMode.EdgeNudge);
        double y = 0, largestStep = 0;
        for (int i = 0; i < 600; i++)
        {
            (_, double next) = camera.Step(0, y, ViewWidth, ViewHeight, 400, 540, Tick);
            largestStep = Math.Max(largestStep, next - y);
            Assert.True(next >= y);
            y = next;
        }

        // At 60 frames a second the fastest frame moves under 3 px (the whole nudge is 108 px).
        Assert.InRange(largestStep, 0.1, 3);
    }

    [Fact]
    public void A_line_written_past_the_right_edge_stays_visible_all_the_way()
    {
        var camera = new CameraFollower(ReplayFollowMode.EdgeNudge);
        double x = 0;
        for (int i = 0; i < 3000; i++)
        {
            double penX = 200 + i * 1.2;   // about 75 px a second at 60 fps
            (x, _) = camera.Step(x, 0, ViewWidth, ViewHeight, penX, 300, Tick);
            Assert.InRange((penX - x) / ViewWidth, 0, 0.93);
        }

        Assert.True(x > 2000);
    }

    [Fact]
    public void A_new_line_starting_back_at_the_left_brings_the_view_back()
    {
        var camera = new CameraFollower(ReplayFollowMode.EdgeNudge);
        double x = 1500;   // the view had followed a long line to the right
        for (int i = 0; i < 600; i++)
        {
            (x, _) = camera.Step(x, 0, ViewWidth, ViewHeight, 100, 300, Tick);
        }

        Assert.Equal(0, x);
    }

    [Fact]
    public void After_a_nudge_the_view_stays_still_while_writing_continues_inside_the_screen()
    {
        var camera = new CameraFollower(ReplayFollowMode.EdgeNudge);
        double x = 1500;

        // New line starts at the far left: the view comes back...
        for (int i = 0; i < 400; i++)
        {
            (x, _) = camera.Step(x, 0, ViewWidth, ViewHeight, 100, 300, Tick);
        }

        Assert.Equal(0, x);

        // ...and then does not trail the pen as it writes across the screen.
        for (double penX = 100; penX < 850; penX += 2)
        {
            (x, _) = camera.Step(x, 0, ViewWidth, ViewHeight, penX, 300, Tick);
            Assert.Equal(0, x);
        }
    }

    [Fact]
    public void Centred_mode_is_unchanged_and_is_the_default()
    {
        var camera = new CameraFollower();
        Assert.Equal(ReplayFollowMode.Centered, camera.Mode);

        double y = 0;
        for (int i = 0; i < 600; i++)
        {
            (_, y) = camera.Step(0, y, ViewWidth, ViewHeight, 400, 540, Tick);
        }

        Assert.Equal(540 - ViewHeight * 0.45, y, 3);
    }
}
