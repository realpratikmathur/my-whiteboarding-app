using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class ShapeGeometryTests
{
    private static double Distance(Point2 a, Point2 b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void Line_includes_both_ends_and_respects_spacing()
    {
        var points = ShapeGeometry.Line(new Point2(0, 0), new Point2(100, 0), spacing: 4);

        Assert.Equal(new Point2(0, 0), points[0]);
        Assert.Equal(new Point2(100, 0), points[^1]);
        Assert.Equal(26, points.Count);
        Assert.All(points, p => Assert.Equal(0, p.Y));
    }

    [Fact]
    public void Polygon_is_closed_and_passes_through_every_corner()
    {
        Point2[] corners = [new(0, 0), new(40, 0), new(40, 20), new(0, 20)];
        var points = ShapeGeometry.ClosedPolygon(corners, spacing: 4);

        Assert.Equal(points[0], points[^1]);
        Assert.All(corners, corner => Assert.Contains(corner, points));
        // Perimeter 120 at spacing 4 is 30 segments, 31 points with the closing point.
        Assert.Equal(31, points.Count);
    }

    [Fact]
    public void Ellipse_points_lie_on_the_ellipse_and_close()
    {
        // Axis-aligned: semi-axes 50 and 20 around (100, 100).
        var points = ShapeGeometry.Ellipse(new Point2(50, 100), new Point2(100, 80), new Point2(150, 100), new Point2(100, 120));

        Assert.True(Distance(points[0], points[^1]) < 1e-9);
        Assert.All(points, p =>
        {
            double value = Math.Pow((p.X - 100) / 50, 2) + Math.Pow((p.Y - 100) / 20, 2);
            Assert.Equal(1, value, 6);
        });
    }

    [Fact]
    public void Rotated_ellipse_follows_its_major_axis()
    {
        // Major axis along the diagonal from (0,0) to (100,100).
        var points = ShapeGeometry.Ellipse(new Point2(0, 0), new Point2(60, 40), new Point2(100, 100), new Point2(40, 60));

        Assert.Contains(points, p => Distance(p, new Point2(100, 100)) < 1e-6);
        Assert.Contains(points, p => Distance(p, new Point2(0, 0)) < 3);
    }

    [Fact]
    public void Slightly_wobbly_stroke_is_a_straight_line()
    {
        var points = Enumerable.Range(0, 50).Select(i => new Point2(i * 4, Math.Sin(i) * 2)).ToList();

        Assert.True(ShapeGeometry.IsStraightLine(points));
    }

    [Fact]
    public void Arc_is_not_a_straight_line()
    {
        var points = Enumerable.Range(0, 50)
            .Select(i => new Point2(i * 4, 40 * Math.Sin(Math.PI * i / 49)))
            .ToList();

        Assert.False(ShapeGeometry.IsStraightLine(points));
    }

    [Fact]
    public void Short_tick_is_not_a_line()
    {
        Assert.False(ShapeGeometry.IsStraightLine([new Point2(0, 0), new Point2(10, 0)]));
    }

    [Fact]
    public void Back_and_forth_scribble_is_not_a_line()
    {
        var points = new List<Point2>();
        for (int pass = 0; pass < 3; pass++)
        {
            IEnumerable<int> xs = Enumerable.Range(0, 26).Select(i => i * 4);
            points.AddRange((pass % 2 == 0 ? xs : xs.Reverse()).Select(x => new Point2(x, 0)));
        }

        Assert.False(ShapeGeometry.IsStraightLine(points));
    }
}
