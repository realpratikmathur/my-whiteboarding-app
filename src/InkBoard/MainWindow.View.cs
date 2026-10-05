using System;
using System.Threading;
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
// Wheel: arrives as an ordinary XAML PointerWheelChanged even over the InkCanvas (verified with real wheel
// messages). Middle button: the InkCanvas would ink or erase with it, so MiddleButtonHook intercepts the
// press before the ink layer sees it and reports the drag here (verified with real mouse input in the
// "realmouse" self-test).
public sealed partial class MainWindow
{
    private MiddleButtonHook? _middleButton;
    private bool _panning;
    private double _panStartX, _panStartY;      // scroll offsets when the drag began
    private int _panStartScreenX, _panStartScreenY;
    private int _panLatestX, _panLatestY, _panMovePending;

    private void ConfigureViewInput()
    {
        // Handled on the page surface, i.e. before the event bubbles to the ScrollViewer, which would scroll.
        PageSurface.PointerWheelChanged += OnPageWheel;

        _middleButton = new MiddleButtonHook(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _middleButton.DragStarted += (x, y) => DispatcherQueue.TryEnqueue(() => BeginDragPan(x, y));
        _middleButton.DragMoved += (x, y) =>
        {
            // Mouse moves can arrive faster than the UI draws: keep only the newest.
            (_panLatestX, _panLatestY) = (x, y);
            if (Interlocked.Exchange(ref _panMovePending, 1) == 0)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    Interlocked.Exchange(ref _panMovePending, 0);
                    DragPanTo(_panLatestX, _panLatestY);
                });
            }
        };
        _middleButton.DragEnded += () => DispatcherQueue.TryEnqueue(EndDragPan);

        CanvasScroller.SizeChanged += (_, _) => UpdateHookPageArea();
        CanvasScroller.LayoutUpdated += (_, _) => UpdateHookPageArea();
        Closed += (_, _) => _middleButton?.Dispose();
    }

    private (int Left, int Top, int Right, int Bottom) _hookArea;

    private void UpdateHookPageArea()
    {
        if (_middleButton is null || Root.XamlRoot is null || CanvasScroller.ActualWidth == 0)
        {
            return;
        }

        double scale = Root.XamlRoot.RasterizationScale;
        Point origin = CanvasScroller.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        var area = (
            (int)(origin.X * scale), (int)(origin.Y * scale),
            (int)((origin.X + CanvasScroller.ViewportWidth) * scale), (int)((origin.Y + CanvasScroller.ViewportHeight) * scale));
        if (area != _hookArea)
        {
            _hookArea = area;
            _middleButton.SetPageArea(area.Item1, area.Item2, area.Item3, area.Item4);
        }
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

    // ---- Middle-button pan (screen pixels from the hook) -----------------------------------------

    internal void BeginDragPan(int screenX, int screenY)
    {
        _panning = true;
        (_panStartScreenX, _panStartScreenY) = (screenX, screenY);
        (_panStartX, _panStartY) = (CanvasScroller.HorizontalOffset, CanvasScroller.VerticalOffset);
    }

    internal void DragPanTo(int screenX, int screenY)
    {
        if (!_panning)
        {
            return;
        }

        double scale = Root.XamlRoot.RasterizationScale;
        (double x, double y) = ViewMath.PanByDrag(
            _panStartX, _panStartY, (screenX - _panStartScreenX) / scale, (screenY - _panStartScreenY) / scale);
        CanvasScroller.ChangeView(x, y, null, disableAnimation: true);
    }

    internal void EndDragPan() => _panning = false;
}
