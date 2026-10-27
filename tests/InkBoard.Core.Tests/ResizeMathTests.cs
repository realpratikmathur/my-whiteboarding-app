using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class ResizeMathTests
{
    // A 200 x 100 picture at (100, 50).
    private static (double X, double Y, double Width, double Height) Drag(ResizeHandle handle, double dx, double dy) =>
        ResizeMath.Resize(100, 50, 200, 100, handle, dx, dy);

    [Fact]
    public void Bottom_right_corner_grows_proportionally_from_the_top_left()
    {
        Assert.Equal((100, 50, 300, 150), Drag(ResizeHandle.BottomRight, 100, 0));
        Assert.Equal((100, 50, 300, 150), Drag(ResizeHandle.BottomRight, 100, 20));   // follows the larger change
    }

    [Fact]
    public void Top_left_corner_keeps_the_bottom_right_corner_in_place()
    {
        var (x, y, w, h) = Drag(ResizeHandle.TopLeft, -100, 0);

        Assert.Equal((300, 150), (w, h));
        Assert.Equal((300, 150), (x + w, y + h));   // bottom-right unchanged
    }

    [Fact]
    public void Other_corners_keep_their_opposite_corner_in_place()
    {
        var (x, y, w, h) = Drag(ResizeHandle.TopRight, 0, -50);       // grows up
        Assert.Equal((100, 150), (x, y + h));                          // bottom-left unchanged
        Assert.Equal(2, w / h, 9);

        (x, y, w, h) = Drag(ResizeHandle.BottomLeft, 100, 0);          // shrinks
        Assert.Equal((300, 50), (x + w, y));                           // top-right unchanged
        Assert.Equal((100, 50), (w, h));
    }

    [Fact]
    public void Edge_handles_stretch_one_direction_only()
    {
        Assert.Equal((100, 50, 260, 100), Drag(ResizeHandle.Right, 60, 999));
        Assert.Equal((100, 50, 200, 130), Drag(ResizeHandle.Bottom, 999, 30));
        Assert.Equal((70, 50, 230, 100), Drag(ResizeHandle.Left, -30, 5));
        Assert.Equal((100, 30, 200, 120), Drag(ResizeHandle.Top, 5, -20));
    }

    [Fact]
    public void A_picture_cannot_be_shrunk_away_or_turned_inside_out()
    {
        var (_, _, w, h) = Drag(ResizeHandle.BottomRight, -1000, -1000);
        Assert.Equal(ResizeMath.MinSize, h);           // the short side stops at the minimum
        Assert.Equal(2, w / h, 9);                     // proportions kept

        (_, _, w, _) = Drag(ResizeHandle.Right, -1000, 0);
        Assert.Equal(ResizeMath.MinSize, w);

        var (x, _, w2, _) = Drag(ResizeHandle.Left, 1000, 0);
        Assert.Equal((300 - ResizeMath.MinSize, ResizeMath.MinSize), (x, w2));   // right edge stays put
    }

    [Fact]
    public void Handles_sit_on_corners_and_edge_midpoints()
    {
        Assert.Equal((100, 50), ResizeMath.HandlePosition(100, 50, 200, 100, ResizeHandle.TopLeft));
        Assert.Equal((200, 50), ResizeMath.HandlePosition(100, 50, 200, 100, ResizeHandle.Top));
        Assert.Equal((300, 100), ResizeMath.HandlePosition(100, 50, 200, 100, ResizeHandle.Right));
        Assert.Equal((300, 150), ResizeMath.HandlePosition(100, 50, 200, 100, ResizeHandle.BottomRight));
        Assert.Equal((100, 100), ResizeMath.HandlePosition(100, 50, 200, 100, ResizeHandle.Left));
        Assert.True(ResizeMath.IsCorner(ResizeHandle.BottomLeft));
        Assert.False(ResizeMath.IsCorner(ResizeHandle.Bottom));
    }
}
