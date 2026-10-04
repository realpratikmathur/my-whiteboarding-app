namespace InkBoard.Core;

/// <summary>
/// Sizing rule for the endless page: it grows to the right and downward in whole chunks so there is always
/// spare room beyond both the content and what is currently on screen. It never shrinks while open.
/// </summary>
public static class PageExtent
{
    public const double MinWidth = 4000;
    public const double MinHeight = 3000;
    public const double Chunk = 2000;

    /// <param name="current">Current page length along one axis.</param>
    /// <param name="contentEdge">Far edge of all ink and objects on that axis.</param>
    /// <param name="viewEdge">Far edge of the visible area on that axis, in page coordinates.</param>
    /// <param name="margin">Room to keep beyond both edges (one viewport).</param>
    public static double Grow(double current, double contentEdge, double viewEdge, double margin, double minimum)
    {
        double needed = Math.Max(contentEdge, viewEdge) + margin;
        double size = Math.Max(current, minimum);
        return needed <= size ? size : Math.Ceiling(needed / Chunk) * Chunk;
    }
}
