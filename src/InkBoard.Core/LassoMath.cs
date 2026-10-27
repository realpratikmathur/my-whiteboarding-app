namespace InkBoard.Core;

/// <summary>Which pictures and text boxes a lasso loop takes in.</summary>
public static class LassoMath
{
    /// <summary>True when the point lies inside the loop (the loop is closed automatically; even-odd rule).</summary>
    public static bool Contains(IReadOnlyList<Point2> loop, Point2 point)
    {
        if (loop.Count < 3)
        {
            return false;
        }

        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            Point2 a = loop[i], b = loop[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// A picture or text box is taken in when its centre is inside the loop (so a loop drawn roughly around it
    /// is enough, as in OneNote).
    /// </summary>
    public static bool TakesIn(IReadOnlyList<Point2> loop, double x, double y, double width, double height) =>
        Contains(loop, new Point2(x + width / 2, y + height / 2));
}
