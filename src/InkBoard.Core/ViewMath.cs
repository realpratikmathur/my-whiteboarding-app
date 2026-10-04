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
    /// Offset that puts the page point grabbed at the start of a drag back under the pointer, given where
    /// the pointer is now in page coordinates. Applying it repeatedly with the same input changes nothing.
    /// </summary>
    public static (double OffsetX, double OffsetY) PanToAnchor(
        double offsetX, double offsetY, double zoom, double anchorPageX, double anchorPageY, double pointerPageX, double pointerPageY) =>
        (Math.Max(0, offsetX + (anchorPageX - pointerPageX) * zoom), Math.Max(0, offsetY + (anchorPageY - pointerPageY) * zoom));
}
