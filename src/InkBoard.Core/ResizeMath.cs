namespace InkBoard.Core;

/// <summary>The eight handles around a selected picture.</summary>
public enum ResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>Moving and resizing a picture by its handles, as in OneNote / Office.</summary>
public static class ResizeMath
{
    /// <summary>Smallest width or height a picture can be resized to, in page units.</summary>
    public const double MinSize = 16;

    public static bool IsCorner(ResizeHandle handle) => (int)handle % 2 == 0;

    /// <summary>Where a handle sits on a rectangle.</summary>
    public static (double X, double Y) HandlePosition(double x, double y, double width, double height, ResizeHandle handle)
    {
        (int col, int row) = Grid(handle);
        return (x + width * col / 2, y + height * row / 2);
    }

    /// <summary>
    /// The rectangle after dragging a handle by (dx, dy). A corner resizes from the opposite corner and keeps
    /// the proportions; an edge handle stretches only the width or only the height, from the opposite edge.
    /// </summary>
    public static (double X, double Y, double Width, double Height) Resize(
        double x, double y, double width, double height, ResizeHandle handle, double dx, double dy)
    {
        (int col, int row) = Grid(handle);

        // Proposed size from the pointer movement; a handle on the left/top grows when moved left/up.
        double newWidth = col switch { 0 => width - dx, 2 => width + dx, _ => width };
        double newHeight = row switch { 0 => height - dy, 2 => height + dy, _ => height };

        if (IsCorner(handle) && width > 0 && height > 0)
        {
            // Follow whichever direction the pointer moved more, and keep the proportions.
            double scaleX = newWidth / width, scaleY = newHeight / height;
            double scale = Math.Abs(scaleX - 1) >= Math.Abs(scaleY - 1) ? scaleX : scaleY;
            scale = Math.Max(scale, MinSize / Math.Min(width, height));
            (newWidth, newHeight) = (width * scale, height * scale);
        }
        else
        {
            newWidth = Math.Max(newWidth, MinSize);
            newHeight = Math.Max(newHeight, MinSize);
        }

        // The opposite side stays where it was.
        double newX = col == 0 ? x + width - newWidth : x;
        double newY = row == 0 ? y + height - newHeight : y;
        return (newX, newY, newWidth, newHeight);
    }

    /// <summary>Column (0 left, 1 middle, 2 right) and row (0 top, 1 middle, 2 bottom) of a handle.</summary>
    private static (int Col, int Row) Grid(ResizeHandle handle) => handle switch
    {
        ResizeHandle.TopLeft => (0, 0),
        ResizeHandle.Top => (1, 0),
        ResizeHandle.TopRight => (2, 0),
        ResizeHandle.Right => (2, 1),
        ResizeHandle.BottomRight => (2, 2),
        ResizeHandle.Bottom => (1, 2),
        ResizeHandle.BottomLeft => (0, 2),
        _ => (0, 1),
    };
}
