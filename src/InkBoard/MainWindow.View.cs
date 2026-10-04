using InkBoard.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace InkBoard;

// Mouse navigation: the wheel zooms (up = in, down = out) around the pointer; holding the middle button
// drags the page.
//
// The InkCanvas owns pen and mouse input on the ink thread, and which route an input it does not turn into
// ink takes is not documented. So both routes are wired to the same view code:
//   - XAML pointer events on the page surface (input the presenter passes through), and
//   - InkUnprocessedInput (input the presenter receives but does not process).
// Both compute an absolute scroll target from the same anchor, so it does not matter if both fire.
public sealed partial class MainWindow
{
    private bool _panning;
    private Point _panAnchor;          // page point grabbed when the middle button went down
    private uint _panPointerId;

    private void ConfigureViewInput()
    {
        // Handled on the page surface, i.e. before the event bubbles to the ScrollViewer, which would scroll.
        PageSurface.PointerWheelChanged += OnPageWheel;
        PageSurface.PointerPressed += OnPagePointerPressed;
        PageSurface.PointerMoved += OnPagePointerMoved;
        PageSurface.PointerReleased += OnPagePointerReleased;
        PageSurface.PointerCaptureLost += (_, _) => _panning = false;
    }

    // ---- Zoom -----------------------------------------------------------------------------------

    private void OnPageWheel(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(CanvasScroller);
        bool control = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);
        if (shift || point.Properties.IsHorizontalMouseWheel)
        {
            return;   // ScrollViewer default: horizontal scroll
        }

        e.Handled = true;
        int delta = point.Properties.MouseWheelDelta;
        if (control)
        {
            // The wheel alone zooms, so Ctrl+wheel takes over plain vertical scrolling.
            CanvasScroller.ChangeView(null, CanvasScroller.VerticalOffset - delta, null, disableAnimation: true);
        }
        else
        {
            ZoomAtViewportPoint(point.Position, delta);
        }
    }

    /// <summary>Zooms by a wheel movement, keeping the page point under the pointer in place.</summary>
    internal void ZoomAtViewportPoint(Point pointer, double wheelDelta)
    {
        (double x, double y, double zoom) = ViewMath.ZoomAt(
            CanvasScroller.HorizontalOffset, CanvasScroller.VerticalOffset, CanvasScroller.ZoomFactor,
            pointer.X, pointer.Y, ViewMath.WheelZoomFactor(wheelDelta),
            CanvasScroller.MinZoomFactor, CanvasScroller.MaxZoomFactor);
        CanvasScroller.ChangeView(x, y, (float)zoom, disableAnimation: true);
    }

    // ---- Middle-button pan ----------------------------------------------------------------------

    /// <summary>Starts a pan with the page point that is under the pointer.</summary>
    internal void BeginPan(Point pagePoint)
    {
        _panning = true;
        _panAnchor = pagePoint;
    }

    /// <summary>Scrolls so that the grabbed page point is under the pointer again.</summary>
    internal void PanTo(Point pointerPagePoint)
    {
        if (!_panning)
        {
            return;
        }

        (double x, double y) = ViewMath.PanToAnchor(
            CanvasScroller.HorizontalOffset, CanvasScroller.VerticalOffset, CanvasScroller.ZoomFactor,
            _panAnchor.X, _panAnchor.Y, pointerPagePoint.X, pointerPagePoint.Y);
        CanvasScroller.ChangeView(x, y, null, disableAnimation: true);
    }

    internal void EndPan() => _panning = false;

    // Route 1: XAML pointer events.
    private void OnPagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(PageSurface);
        if (point.PointerDeviceType == PointerDeviceType.Mouse && point.Properties.IsMiddleButtonPressed)
        {
            BeginPan(point.Position);
            _panPointerId = point.PointerId;
            PageSurface.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnPagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_panning && e.Pointer.PointerId == _panPointerId)
        {
            PanTo(e.GetCurrentPoint(PageSurface).Position);
            e.Handled = true;
        }
    }

    private void OnPagePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panning && e.Pointer.PointerId == _panPointerId)
        {
            EndPan();
            PageSurface.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    // Route 2: input the presenter received but did not process (see ConfigureCanvasBehaviours).
    private static bool IsMiddleButton(Windows.UI.Core.PointerEventArgs args) =>
        args.CurrentPoint.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse
        && args.CurrentPoint.Properties.IsMiddleButtonPressed;
}
