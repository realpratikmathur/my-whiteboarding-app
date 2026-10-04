namespace InkBoard.Core;

public readonly record struct Point2(double X, double Y);

/// <summary>A run of consecutive original points: indices [Start, Start + Count).</summary>
public readonly record struct PointRange(int Start, int Count);

/// <summary>
/// Decides which of a stroke's original points survive a pass of the point eraser. It only selects runs of
/// existing points; it never moves, resamples or smooths them, so the platform re-renders each fragment
/// from untouched input data.
/// </summary>
public static class StrokeSplitter
{
    /// <summary>
    /// Splits a stroke against one eraser movement (the segment <paramref name="eraserFrom"/> to
    /// <paramref name="eraserTo"/>, swept with <paramref name="radius"/>).
    /// </summary>
    /// <returns>
    /// The surviving runs in order. A single range covering every point means the stroke was not touched;
    /// an empty list means it was erased completely.
    /// </returns>
    public static IReadOnlyList<PointRange> Split(
        IReadOnlyList<Point2> points, Point2 eraserFrom, Point2 eraserTo, double radius)
    {
        var runs = new List<PointRange>();
        int runStart = -1;

        for (int i = 0; i < points.Count; i++)
        {
            bool erased = DistanceToSegment(points[i], eraserFrom, eraserTo) <= radius;
            if (!erased && runStart < 0)
            {
                runStart = i;
            }

            bool isLast = i == points.Count - 1;
            // The eraser can pass between two widely spaced points without touching either.
            bool cutAfter = !isLast
                && SegmentDistance(points[i], points[i + 1], eraserFrom, eraserTo) <= radius;

            if (runStart >= 0 && (erased || cutAfter || isLast))
            {
                int end = erased ? i : i + 1;
                AddRun(runs, runStart, end - runStart, points.Count);
                runStart = -1;
            }
        }

        return runs;
    }

    public static bool IsUntouched(IReadOnlyList<PointRange> runs, int pointCount) =>
        runs.Count == 1 && runs[0].Start == 0 && runs[0].Count == pointCount;

    private static void AddRun(List<PointRange> runs, int start, int count, int totalPoints)
    {
        // A lone leftover point would render as a speck of dust; a stroke that was a single dot is kept.
        if (count >= 2 || (count == 1 && totalPoints == 1))
        {
            runs.Add(new PointRange(start, count));
        }
    }

    internal static double DistanceToSegment(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }

    internal static double SegmentDistance(Point2 a1, Point2 a2, Point2 b1, Point2 b2)
    {
        if (Intersect(a1, a2, b1, b2))
        {
            return 0;
        }

        return Math.Min(
            Math.Min(DistanceToSegment(a1, b1, b2), DistanceToSegment(a2, b1, b2)),
            Math.Min(DistanceToSegment(b1, a1, a2), DistanceToSegment(b2, a1, a2)));
    }

    private static bool Intersect(Point2 a1, Point2 a2, Point2 b1, Point2 b2)
    {
        double d1 = Cross(b1, b2, a1), d2 = Cross(b1, b2, a2);
        double d3 = Cross(a1, a2, b1), d4 = Cross(a1, a2, b2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross(Point2 origin, Point2 a, Point2 b) =>
        (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);
}
