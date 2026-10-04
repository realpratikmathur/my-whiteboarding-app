using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class StrokeSplitterTests
{
    // Horizontal stroke: (0,0), (10,0), ... (90,0)
    private static List<Point2> Line(int count = 10, double spacing = 10) =>
        Enumerable.Range(0, count).Select(i => new Point2(i * spacing, 0)).ToList();

    private static IReadOnlyList<PointRange> EraseAt(List<Point2> points, double x, double y, double radius) =>
        StrokeSplitter.Split(points, new Point2(x, y), new Point2(x, y), radius);

    [Fact]
    public void Miss_leaves_stroke_untouched()
    {
        var points = Line();
        var runs = EraseAt(points, 45, 50, 5);

        Assert.True(StrokeSplitter.IsUntouched(runs, points.Count));
    }

    [Fact]
    public void Hit_in_the_middle_splits_into_two_runs_of_original_points()
    {
        var points = Line();
        var runs = EraseAt(points, 50, 0, 3);

        Assert.Equal([new PointRange(0, 5), new PointRange(6, 4)], runs);
    }

    [Fact]
    public void Hit_at_the_start_trims_the_start()
    {
        var runs = EraseAt(Line(), 0, 0, 12);

        Assert.Equal([new PointRange(2, 8)], runs);
    }

    [Fact]
    public void Hit_at_the_end_trims_the_end()
    {
        var runs = EraseAt(Line(), 90, 0, 3);

        Assert.Equal([new PointRange(0, 9)], runs);
    }

    [Fact]
    public void Eraser_covering_everything_leaves_nothing()
    {
        var points = Line();
        var runs = StrokeSplitter.Split(points, new Point2(0, 0), new Point2(90, 0), 2);

        Assert.Empty(runs);
    }

    [Fact]
    public void Eraser_passing_between_sparse_points_still_cuts()
    {
        // Crosses the stroke at x = 45, midway between the points at 40 and 50, touching neither.
        var points = Line();
        var runs = StrokeSplitter.Split(points, new Point2(45, -20), new Point2(45, 20), 1);

        Assert.Equal([new PointRange(0, 5), new PointRange(5, 5)], runs);
    }

    [Fact]
    public void Single_leftover_points_are_dropped()
    {
        // Erasing the point at 10 leaves the point at 0 on its own.
        var runs = EraseAt(Line(), 10, 0, 3);

        Assert.Equal([new PointRange(2, 8)], runs);
    }

    [Fact]
    public void Untouched_dot_survives()
    {
        var dot = new List<Point2> { new(5, 5) };
        var runs = EraseAt(dot, 50, 50, 3);

        Assert.True(StrokeSplitter.IsUntouched(runs, 1));
    }

    [Fact]
    public void Erased_dot_is_removed()
    {
        var dot = new List<Point2> { new(5, 5) };

        Assert.Empty(EraseAt(dot, 5, 5, 3));
    }

    [Fact]
    public void Two_hits_in_one_sweep_give_three_runs()
    {
        // A "V" sweep is two calls; here one long diagonal sweep crosses a zig-zag stroke twice.
        var points = new List<Point2>
        {
            new(0, 0), new(10, 0), new(20, 0), new(30, 0), new(30, 10), new(30, 20),
            new(20, 20), new(10, 20), new(0, 20),
        };
        var runs = StrokeSplitter.Split(points, new Point2(15, -10), new Point2(15, 30), 1);

        Assert.Equal([new PointRange(0, 2), new PointRange(2, 5), new PointRange(7, 2)], runs);
    }
}
