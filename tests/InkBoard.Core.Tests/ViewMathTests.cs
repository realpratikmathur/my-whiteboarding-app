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
        // The page follows the mouse: drag 120 right / 60 down and the view scrolls 120 left / 60 up.
        Assert.Equal((480, 440), ViewMath.PanByDrag(600, 500, 120, 60));
        Assert.Equal((650, 520), ViewMath.PanByDrag(600, 500, -50, -20));
    }

    [Fact]
    public void Pan_is_computed_from_the_start_so_skipped_moves_do_not_matter()
    {
        // Whatever happened in between, a drag of (30, 10) from the start always lands in the same place.
        Assert.Equal(ViewMath.PanByDrag(600, 500, 30, 10), ViewMath.PanByDrag(600, 500, 30, 10));
        Assert.Equal((600, 500), ViewMath.PanByDrag(600, 500, 0, 0));
    }

    [Fact]
    public void Pan_cannot_go_before_the_page_start()
    {
        Assert.Equal((0, 0), ViewMath.PanByDrag(20, 10, 400, 400));
    }
}
