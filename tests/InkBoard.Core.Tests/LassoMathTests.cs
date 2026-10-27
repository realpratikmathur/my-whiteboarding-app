using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class LassoMathTests
{
    // A square loop from (0,0) to (100,100), not explicitly closed (as a hand-drawn lasso usually is not).
    private static readonly Point2[] Square = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];

    [Fact]
    public void Points_inside_and_outside_a_loop()
    {
        Assert.True(LassoMath.Contains(Square, new Point2(50, 50)));
        Assert.False(LassoMath.Contains(Square, new Point2(150, 50)));
        Assert.False(LassoMath.Contains(Square, new Point2(50, -10)));
    }

    [Fact]
    public void Concave_loops_work()
    {
        // A "U": the gap between its arms is outside.
        Point2[] u = [new(0, 0), new(30, 0), new(30, 70), new(70, 70), new(70, 0), new(100, 0), new(100, 100), new(0, 100)];

        Assert.True(LassoMath.Contains(u, new Point2(15, 50)));
        Assert.False(LassoMath.Contains(u, new Point2(50, 30)));
        Assert.True(LassoMath.Contains(u, new Point2(50, 85)));
    }

    [Fact]
    public void A_picture_is_taken_in_when_its_centre_is_inside()
    {
        Assert.True(LassoMath.TakesIn(Square, 20, 20, 60, 60));
        Assert.True(LassoMath.TakesIn(Square, 30, 30, 100, 40));    // sticks out, but its centre (80,50) is inside
        Assert.False(LassoMath.TakesIn(Square, 120, 20, 50, 50));
    }

    [Fact]
    public void A_scribble_too_short_to_be_a_loop_takes_in_nothing()
    {
        Assert.False(LassoMath.Contains([new(0, 0), new(100, 100)], new Point2(50, 50)));
    }
}
