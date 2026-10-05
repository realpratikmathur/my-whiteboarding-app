namespace InkBoard.Core;

/// <summary>
/// Scroll-offset arithmetic for mouse zooming and panning. Offsets are in zoomed (screen) pixels, as a
/// ScrollViewer reports them; page coordinates are unzoomed.
/// </summary>
public static class ViewMath
{
    /// <summary>Zoom change for one wheel notch (120 units).</summary>
    public const double WheelStep = 1.1;

    /// <summary>Wheel up (positive) zooms in, wheel down zooms out; partial notches scale smoothly.</summary>
    public static double WheelZoomFactor(double wheelDelta) => Math.Pow(WheelStep, wheelDelta / 120.0);

    /// <summary>
    /// Zooms by <paramref name="factor"/> keeping the page point under the pointer where it is on screen.
    /// </summary>
    /// <param name="pointerX">Pointer position inside the viewport, in screen pixels.</param>
    public static (double OffsetX, double OffsetY, double Zoom) ZoomAt(
        double offsetX, double offsetY, double zoom, double pointerX, double pointerY, double factor, double minZoom, double maxZoom)
    {
        double newZoom = Math.Clamp(zoom * factor, minZoom, maxZoom);
        double pageX = (offsetX + pointerX) / zoom, pageY = (offsetY + pointerY) / zoom;
        return (Math.Max(0, pageX * newZoom - pointerX), Math.Max(0, pageY * newZoom - pointerY), newZoom);
    }

    /// <summary>
    /// Scroll offset for a drag-pan: the page follows the mouse, so the view moves the opposite way by the
    /// distance dragged since the button went down. Computed from the starting offset each time, so it
    /// cannot drift however many moves are skipped.
    /// </summary>
    /// <param name="dragX">How far the pointer has moved since the drag began, in screen (DIP) units.</param>
    public static (double OffsetX, double OffsetY) PanByDrag(double startOffsetX, double startOffsetY, double dragX, double dragY) =>
        (Math.Max(0, startOffsetX - dragX), Math.Max(0, startOffsetY - dragY));
}
