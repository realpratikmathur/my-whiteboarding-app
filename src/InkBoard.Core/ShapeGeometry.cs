namespace InkBoard.Core;

/// <summary>
/// Ideal outlines for "Ink to shape". These are the point positions handed to the platform stroke builder;
/// the stroke itself is still built and rendered by the platform.
/// </summary>
public static class ShapeGeometry
{
    /// <summary>Points along a straight segment, both ends included, roughly <paramref name="spacing"/> apart.</summary>
    public static IReadOnlyList<Point2> Line(Point2 from, Point2 to, double spacing = 4)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(Distance(from, to) / spacing));
        var points = new List<Point2>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            points.Add(new Point2(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t));
        }

        return points;
    }

    /// <summary>A closed outline through the corners, ending where it starts.</summary>
    public static IReadOnlyList<Point2> ClosedPolygon(IReadOnlyList<Point2> corners, double spacing = 4)
    {
        var points = new List<Point2>();
        for (int i = 0; i < corners.Count; i++)
        {
            IReadOnlyList<Point2> edge = Line(corners[i], corners[(i + 1) % corners.Count], spacing);
            // Each edge's last point is the next edge's first.
            points.AddRange(i == corners.Count - 1 ? edge : edge.Take(edge.Count - 1));
        }

        return points;
    }

    /// <summary>
    /// A closed ellipse given the two end points of each axis (the layout InkAnalyzer reports:
    /// points 0 and 2 on one axis, 1 and 3 on the other).
    /// </summary>
    public static IReadOnlyList<Point2> Ellipse(Point2 axisAStart, Point2 axisBStart, Point2 axisAEnd, Point2 axisBEnd, double spacing = 4)
    {
        var center = new Point2((axisAStart.X + axisAEnd.X) / 2, (axisAStart.Y + axisAEnd.Y) / 2);
        double a = Distance(axisAStart, axisAEnd) / 2, b = Distance(axisBStart, axisBEnd) / 2;
        double angle = Math.Atan2(axisAEnd.Y - axisAStart.Y, axisAEnd.X - axisAStart.X);
        double cos = Math.Cos(angle), sin = Math.Sin(angle);

        // Ramanujan's approximation of the circumference, to pick the point count.
        double circumference = Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b)));
        int steps = Math.Max(24, (int)Math.Ceiling(circumference / spacing));

        var points = new List<Point2>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double t = 2 * Math.PI * i / steps;
            double x = a * Math.Cos(t), y = b * Math.Sin(t);
            points.Add(new Point2(center.X + x * cos - y * sin, center.Y + x * sin + y * cos));
        }

        return points;
    }

    /// <summary>
    /// True when a hand-drawn stroke is close enough to its own chord to be meant as a straight line.
    /// InkAnalyzer has no "line" drawing kind, so this is the only shape the app has to judge itself.
    /// </summary>
    public static bool IsStraightLine(IReadOnlyList<Point2> points, double minLength = 30)
    {
        if (points.Count < 2)
        {
            return false;
        }

        Point2 first = points[0], last = points[^1];
        double chord = Distance(first, last);
        if (chord < minLength)
        {
            return false;
        }

        double tolerance = Math.Max(4, chord * 0.05);
        double pathLength = 0;
        for (int i = 0; i < points.Count; i++)
        {
            if (StrokeSplitter.DistanceToSegment(points[i], first, last) > tolerance)
            {
                return false;
            }

            if (i > 0)
            {
                pathLength += Distance(points[i - 1], points[i]);
            }
        }

        // Rejects scribbling back and forth along the same line.
        return pathLength <= chord * 1.2;
    }

    private static double Distance(Point2 a, Point2 b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
