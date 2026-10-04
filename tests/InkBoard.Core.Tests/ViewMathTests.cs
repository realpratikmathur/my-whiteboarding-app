using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class ViewMathTests
{
    [Fact]
    public void Wheel_up_zooms_in_and_wheel_down_zooms_out_by_the_same_ratio()
    {
        Assert.Equal(1.1, ViewMath.WheelZoomFactor(120), 9);
        Assert.Equal(1 / 1.1, ViewMath.WheelZoomFactor(-120), 9);
        Assert.Equal(1, ViewMath.WheelZoomFactor(120) * ViewMath.WheelZoomFactor(-120), 9);
    }

    [Fact]
    public void Small_wheel_deltas_zoom_proportionally()
    {
        // A precision wheel or touchpad sends fractions of a notch.
        Assert.Equal(ViewMath.WheelZoomFactor(120), Math.Pow(ViewMath.WheelZoomFactor(30), 4), 9);
    }

    [Fact]
    public void Zooming_keeps_the_page_point_under_the_pointer()
    {
        // Pointer at (300, 200) in the viewport; view scrolled to (1000, 500) at 100%.
        (double x, double y, double zoom) = ViewMath.ZoomAt(1000, 500, 1, 300, 200, 2, 0.25, 8);

        Assert.Equal(2, zoom);
        // Page point under the pointer before: (1300, 700). After: (offset + pointer) / zoom must be the same.
        Assert.Equal(1300, (x + 300) / zoom, 9);
        Assert.Equal(700, (y + 200) / zoom, 9);
    }

    [Fact]
    public void Zoom_stops_at_the_limits()
    {
        Assert.Equal(8, ViewMath.ZoomAt(0, 0, 7.9, 100, 100, 1.1, 0.25, 8).Zoom);
        Assert.Equal(0.25, ViewMath.ZoomAt(0, 0, 0.26, 100, 100, 1 / 1.1, 0.25, 8).Zoom);
    }

    [Fact]
    public void Zooming_out_near_the_origin_does_not_scroll_before_the_page_start()
    {
        (double x, double y, _) = ViewMath.ZoomAt(10, 10, 1, 500, 400, 0.5, 0.25, 8);

        Assert.Equal((0, 0), (x, y));
    }

    [Fact]
    public void Dragging_right_and_down_moves_the_view_left_and_up()
    {
        // Grabbed page point (500, 400); the pointer is now over page point (450, 380), i.e. the mouse moved
        // 50 left and 20 up on the page, so the view must scroll 50 right and 20 down to follow... and back.
        (double x, double y) = ViewMath.PanToAnchor(1000, 800, 1, 500, 400, 450, 380);
        Assert.Equal((1050, 820), (x, y));

        (x, y) = ViewMath.PanToAnchor(1000, 800, 1, 500, 400, 560, 430);
        Assert.Equal((940, 770), (x, y));
    }

    [Fact]
    public void Pan_accounts_for_zoom_and_settles_once_the_anchor_is_under_the_pointer()
    {
        (double x, double y) = ViewMath.PanToAnchor(1000, 800, 2, 500, 400, 450, 380);
        Assert.Equal((1100, 840), (x, y));

        // After that scroll the pointer is over the anchor again: nothing more to do.
        Assert.Equal((1100, 840), ViewMath.PanToAnchor(1100, 840, 2, 500, 400, 500, 400));
    }

    [Fact]
    public void Pan_cannot_go_before_the_page_start()
    {
        Assert.Equal((0, 0), ViewMath.PanToAnchor(20, 10, 1, 100, 100, 400, 400));
    }
}
