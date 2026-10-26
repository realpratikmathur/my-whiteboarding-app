using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class FollowOffTests
{
    [Theory]
    [InlineData(500, 300)]       // middle of the screen
    [InlineData(990, 590)]       // bottom-right edge
    [InlineData(5000, 9000)]     // far off screen
    [InlineData(-200, -50)]      // above and left of the view
    public void Off_never_moves_the_view(double targetX, double targetY)
    {
        var camera = new CameraFollower(ReplayFollowMode.Off);
        for (int i = 0; i < 300; i++)
        {
            Assert.Equal((120, 340), camera.Step(120, 340, 1000, 600, targetX, targetY, 16));
        }
    }

    [Fact]
    public void Off_leaves_a_view_the_presenter_moved_where_it_is()
    {
        var camera = new CameraFollower(ReplayFollowMode.Off);
        camera.Step(0, 0, 1000, 600, 2000, 2000, 16);

        // The presenter scrolled somewhere else meanwhile: the next step keeps that.
        Assert.Equal((777, 888), camera.Step(777, 888, 1000, 600, 2000, 2000, 16));
    }

    [Fact]
    public void Existing_choices_keep_their_stored_numbers()
    {
        // notebook.json stores the follow mode as a number; the new choice must not renumber the others.
        Assert.Equal(0, (int)ReplayFollowMode.Centered);
        Assert.Equal(1, (int)ReplayFollowMode.EdgeNudge);
        Assert.Equal(2, (int)ReplayFollowMode.Off);
        Assert.Equal(ReplayFollowMode.Off, Notebook.FromJson("""{"Pages":[],"ReplayFollow":2}""").ReplayFollow);
    }
}
