using System;
using System.Collections.Generic;
using System.Linq;
using InkBoard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI.Core;
using InkStroke = Windows.UI.Input.Inking.InkStroke;

namespace InkBoard;

// Canvas behaviours: undo/redo, lasso selection, point eraser, touch toggle, zoom.
public sealed partial class MainWindow
{
    private const double PointEraserRadius = 8;

    private InkDocument _document = null!;
    private UndoStack _undo = null!;

    private bool _toolPointerDown;
    private Point _lastToolPoint;

    private List<Point>? _lassoPoints;
    private Rect? _selectionBounds;
    private bool _draggingSelection;
    private Point _dragTotal;

    private PartialEraseSession? _eraseSession;

    private void ConfigureCanvasBehaviours()
    {
        _document = new InkDocument(Presenter.StrokeContainer);
        _undo = new UndoStack(_document);
        _undo.Changed += (_, _) => UpdateUndoButtons();
        UpdateUndoButtons();

        // Undo history is driven by what the platform reports it collected and erased.
        Presenter.StrokesCollected += (_, args) =>
        {
            List<InkStroke> strokes = args.Strokes.ToList();
            DispatcherQueue.TryEnqueue(() => _undo.Push(new AddStrokesStep(_document.Track(strokes)) { Drawn = true }));
        };
        Presenter.StrokesErased += (_, args) =>
        {
            List<InkStroke> strokes = args.Strokes.ToList();
            DispatcherQueue.TryEnqueue(() =>
            {
                IReadOnlyList<int> removed = _document.MarkRemoved(strokes);
                if (removed.Count > 0)
                {
                    _undo.Push(new RemoveStrokesStep(removed));
                }
            });
        };
        Toolbar.EraseAllClicked += (_, _) => EraseAll();

        // Lasso and point eraser work on input the presenter leaves unprocessed (Mode = None).
        // (Middle-button drags never get here: MiddleButtonHook takes them for panning.)
        Presenter.UnprocessedInput.PointerPressed += (_, args) => RouteToolInput(args, OnToolPressed);
        Presenter.UnprocessedInput.PointerMoved += (_, args) => RouteToolInput(args, OnToolMoved);
        Presenter.UnprocessedInput.PointerReleased += (_, args) => RouteToolInput(args, OnToolReleased, evenWithoutPosition: true);
        Presenter.UnprocessedInput.PointerLost += (_, args) => RouteToolInput(args, OnToolReleased, evenWithoutPosition: true);

        CanvasScroller.ViewChanged += (_, _) =>
        {
            ZoomText.Text = $"{CanvasScroller.ZoomFactor * 100:0}%";
            UpdatePageExtent();
            UpdateObjectChrome();   // picture handles keep their size on screen
        };
        CanvasScroller.SizeChanged += (_, _) => UpdatePageExtent();
        _undo.Changed += (_, _) => OnPageContentChanged();
        ApplyInputDevices();
    }

    // ---- Endless page ---------------------------------------------------------------------------

    private Rect _contentBounds;

    private void OnPageContentChanged()
    {
        Rect bounds = _document.Container.BoundingRect;
        bounds.Union(ObjectBounds());
        _contentBounds = bounds;
        MarkPageDirty();
        UpdatePageExtent();
    }

    /// <summary>Extends the page to the right and downward so it never runs out; see <see cref="PageExtent"/>.</summary>
    private void UpdatePageExtent()
    {
        float zoom = CanvasScroller.ZoomFactor;
        double viewWidth = CanvasScroller.ViewportWidth / zoom, viewHeight = CanvasScroller.ViewportHeight / zoom;
        double width = PageExtent.Grow(
            PageSurface.Width, _contentBounds.Right, CanvasScroller.HorizontalOffset / zoom + viewWidth, viewWidth, PageExtent.MinWidth);
        double height = PageExtent.Grow(
            PageSurface.Height, _contentBounds.Bottom, CanvasScroller.VerticalOffset / zoom + viewHeight, viewHeight, PageExtent.MinHeight);
        if (width != PageSurface.Width || height != PageSurface.Height)
        {
            PageSurface.Width = width;
            PageSurface.Height = height;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32")]
    private static extern bool GetCursorPos(out POINT point);

    [System.Runtime.InteropServices.DllImport("user32")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [System.Runtime.InteropServices.DllImport("user32")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private static bool IsRightButtonDown => (GetAsyncKeyState(0x02) & 0x8000) != 0;

    /// <summary>
    /// Where the pointer is on the page right now, from the system cursor (which follows mouse and pen).
    ///
    /// This replaces the pointer data of ink events. On WindowsAppSDK 2.5.4-experimental the
    /// InkStrokeInput / InkUnprocessedInput events are raised (on the UI thread) but their
    /// Windows.UI.Core.PointerEventArgs carry no readable point: CurrentPoint and every item of
    /// GetIntermediatePoints() fail with 0x80004021 (found by driving the app with real mouse input). The
    /// events therefore only say WHEN something happened; this says where.
    /// </summary>
    private Point? PointerOnPage()
    {
        if (Root.XamlRoot is null || !GetCursorPos(out POINT cursor))
        {
            return null;
        }

        ScreenToClient(WinRT.Interop.WindowNative.GetWindowHandle(this), ref cursor);
        double scale = Root.XamlRoot.RasterizationScale;
        return Root.TransformToVisual(PageSurface).TransformPoint(new Point(cursor.X / scale, cursor.Y / scale));
    }

    private void RouteToolInput(PointerEventArgs args, System.Action<Point> handler, bool evenWithoutPosition = false)
    {
        // Nothing here may throw: an exception escaping one handler of an ink event also stops the handlers
        // registered after it from being called.
        Point? position = PointerOnPage();
        if (position is null && !evenWithoutPosition)
        {
            return;
        }

        // A release must always be delivered, if need be at the last known position.
        handler(position ?? _lastToolPoint);
    }

    // ---- Barrel-button / right-button drag: erase whole strokes ---------------------------------

    private const double StrokeEraserRadius = 6;

    private List<int>? _erasedByDrag;

    /// <summary>Removes every stroke the pointer passed over between two positions.</summary>
    private void EraseStrokesAlong(Point from, Point to)
    {
        var hit = new List<int>();
        foreach (InkStroke stroke in _document.Container.GetStrokes())
        {
            Size tip = stroke.DrawingAttributes.Size;
            double radius = StrokeEraserRadius + System.Math.Max(tip.Width, tip.Height) / 2;
            Rect bounds = stroke.BoundingRect;
            if (bounds.Right < System.Math.Min(from.X, to.X) - radius || bounds.Left > System.Math.Max(from.X, to.X) + radius
                || bounds.Bottom < System.Math.Min(from.Y, to.Y) - radius || bounds.Top > System.Math.Max(from.Y, to.Y) + radius)
            {
                continue;
            }

            List<Point2> positions = InkDocument.CanvasPositions(stroke);
            var runs = StrokeSplitter.Split(positions, new Point2(from.X, from.Y), new Point2(to.X, to.Y), radius);
            if (!StrokeSplitter.IsUntouched(runs, positions.Count))
            {
                hit.Add(_document.Track(stroke));
            }
        }

        if (hit.Count > 0)
        {
            _document.Remove(hit);
            _erasedByDrag!.AddRange(hit);
        }
    }

    private bool LassoActive => ReferenceEquals(Toolbar.ActiveTool, LassoButton);
    private bool PointEraserActive => ReferenceEquals(Toolbar.ActiveTool, PointEraserButton);

    private InkInputProcessingMode ModeForActiveTool() => Toolbar.ActiveTool?.ToolKind switch
    {
        InkToolbarTool.Eraser => InkInputProcessingMode.Erasing,
        InkToolbarTool.CustomTool => InkInputProcessingMode.None,
        _ => InkInputProcessingMode.Inking,
    };

    private void OnActiveToolChangedForCanvas()
    {
        Presenter.InputProcessingConfiguration.Mode = ModeForActiveTool();
        UpdateTextFormatBar();
        if (!LassoActive)
        {
            ClearSelection();
        }

        if (!ObjectToolActive)
        {
            // Ends any text edit and drops the object selection.
            Root.Focus(FocusState.Programmatic);
            SelectObject(null);
        }
    }

    // ---- Tool input -----------------------------------------------------------------------------

    private void OnToolPressed(Point position)
    {
        _toolPointerDown = true;
        _lastToolPoint = position;

        // With a pen or eraser tool selected, the only presses the presenter leaves unprocessed are
        // barrel-button / right-button drags. With the other tools the button has to be checked.
        if (ModeForActiveTool() != InkInputProcessingMode.None || IsRightButtonDown)
        {
            ClearSelection();
            _erasedByDrag = [];
            EraseStrokesAlong(position, position);
        }
        else if (ObjectToolActive)
        {
            OnObjectToolPressed(position);
        }
        else if (LassoActive)
        {
            if (_selectionBounds is Rect bounds && bounds.Contains(position))
            {
                _draggingSelection = true;
                _dragTotal = default;
            }
            else
            {
                ClearSelection();
                _lassoPoints = [position];
                LassoLine.Points.Clear();
                LassoLine.Points.Add(position);
                LassoLine.Visibility = Visibility.Visible;
            }
        }
        else if (PointEraserActive)
        {
            _eraseSession = new PartialEraseSession(_document);
            _eraseSession.EraseAlong(position, position, PointEraserRadius);
        }
    }

    private void OnToolMoved(Point position)
    {
        if (!_toolPointerDown)
        {
            return;
        }

        if (_erasedByDrag is not null)
        {
            EraseStrokesAlong(_lastToolPoint, position);
        }
        else if (ObjectToolActive)
        {
            OnObjectToolMoved(position);
        }
        else if (_draggingSelection)
        {
            var delta = new Point(position.X - _lastToolPoint.X, position.Y - _lastToolPoint.Y);
            Rect? ink = _document.SelectedHandles().Count > 0 ? _document.Container.MoveSelected(delta) : null;
            MoveObjects(_lassoObjects, delta.X, delta.Y);
            ShowSelection(SelectionArea(ink));
            _dragTotal = new Point(_dragTotal.X + delta.X, _dragTotal.Y + delta.Y);
        }
        else if (_lassoPoints is not null)
        {
            _lassoPoints.Add(position);
            LassoLine.Points.Add(position);
        }
        else
        {
            _eraseSession?.EraseAlong(_lastToolPoint, position, PointEraserRadius);
        }

        _lastToolPoint = position;
    }

    private void OnToolReleased(Point position)
    {
        if (!_toolPointerDown)
        {
            return;
        }

        _toolPointerDown = false;

        if (_erasedByDrag is List<int> erased)
        {
            _erasedByDrag = null;
            if (erased.Count > 0)
            {
                _undo.Push(new RemoveStrokesStep(erased));
            }
        }
        else if (ObjectToolActive)
        {
            OnObjectToolReleased();
        }
        else if (_draggingSelection)
        {
            _draggingSelection = false;
            if (_dragTotal != default)
            {
                IReadOnlyList<int> strokes = _document.SelectedHandles();
                List<PageObject> objects = [.. _lassoObjects];
                (double dx, double dy) = (_dragTotal.X, _dragTotal.Y);
                var objectMove = new ActionStep(() => MoveObjects(objects, -dx, -dy), () => MoveObjects(objects, dx, dy));
                _undo.Push(strokes.Count == 0
                    ? objectMove
                    : objects.Count == 0
                        ? new MoveStrokesStep(strokes, dx, dy)
                        : new CompanionStep(new MoveStrokesStep(strokes, dx, dy), objectMove.UndoAction, objectMove.RedoAction));
            }
        }
        else if (_lassoPoints is not null)
        {
            Rect ink = _document.Container.SelectWithPolyLine(_lassoPoints);
            List<Point2> loop = _lassoPoints.Select(p => new Point2(p.X, p.Y)).ToList();
            _lassoObjects = _objects.Where(o => LassoMath.TakesIn(loop, o.X, o.Y, o.Width, o.Height)).ToList();
            _lassoPoints = null;
            LassoLine.Visibility = Visibility.Collapsed;
            ShowSelection(SelectionArea(ink.Width > 0 || ink.Height > 0 ? ink : null));
        }
        else if (_eraseSession is not null)
        {
            if (_eraseSession.Finish() is IUndoStep step)
            {
                _undo.Push(step);
            }

            _eraseSession = null;
        }
    }

    // ---- Selection ------------------------------------------------------------------------------

    /// <summary>Pictures and text boxes taken in by the lasso (moved and deleted together with the ink).</summary>
    private List<PageObject> _lassoObjects = [];

    /// <summary>The box around the lassoed ink and the lassoed pictures and text boxes.</summary>
    private Rect SelectionArea(Rect? ink)
    {
        Rect? area = ink;
        foreach (PageObject obj in _lassoObjects)
        {
            var rect = new Rect(obj.X, obj.Y, Math.Max(obj.Width, 1), Math.Max(obj.Height, 1));
            if (area is Rect so)
            {
                rect.Union(so);
            }

            area = rect;
        }

        return area ?? Rect.Empty;
    }

    private void MoveObjects(IEnumerable<PageObject> objects, double dx, double dy)
    {
        foreach (PageObject obj in objects)
        {
            SetObjectRect(obj, new Rect(obj.X + dx, obj.Y + dy, Math.Max(obj.Width, 1), Math.Max(obj.Height, 1)));
        }
    }

    private void ShowSelection(Rect bounds)
    {
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            _selectionBounds = null;
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        _selectionBounds = bounds;
        Canvas.SetLeft(SelectionRect, bounds.X - 4);
        Canvas.SetTop(SelectionRect, bounds.Y - 4);
        SelectionRect.Width = bounds.Width + 8;
        SelectionRect.Height = bounds.Height + 8;
        SelectionRect.Visibility = Visibility.Visible;
    }

    private void ClearSelection()
    {
        if (_selectionBounds is not null)
        {
            _document.ClearSelection();
        }

        _selectionBounds = null;
        _lassoPoints = null;
        _lassoObjects = [];
        _draggingSelection = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        LassoLine.Visibility = Visibility.Collapsed;
    }

    private void DeleteSelection()
    {
        IReadOnlyList<int> selected = _document.SelectedHandles();
        List<PageObject> objects = [.. _lassoObjects];
        if (selected.Count == 0 && objects.Count == 0)
        {
            return;
        }

        void HideAll() => objects.ForEach(HideObject);
        void ShowAll() => objects.ForEach(ShowObject);
        if (selected.Count > 0)
        {
            _document.Remove(selected);
        }

        HideAll();
        _undo.Push(selected.Count == 0
            ? new ActionStep(ShowAll, HideAll)
            : objects.Count == 0
                ? new RemoveStrokesStep(selected)
                : new CompanionStep(new RemoveStrokesStep(selected), ShowAll, HideAll));
        ClearSelection();
    }

    private void EraseAll()
    {
        // The toolbar clears the container itself without the presenter reporting the strokes as erased;
        // ClearAll also covers the case where it has not cleared it yet.
        IReadOnlyList<int> removed = _document.ClearAll();
        if (removed.Count > 0)
        {
            _undo.Push(new RemoveStrokesStep(removed));
        }

        ClearSelection();
    }

    // ---- Undo / redo ----------------------------------------------------------------------------

    private void Undo()
    {
        if (_replaying)
        {
            return;
        }

        ClearSelection();
        _undo.Undo();
        _document.ClearSelection();
    }

    private void Redo()
    {
        if (_replaying)
        {
            return;
        }

        ClearSelection();
        _undo.Redo();
        _document.ClearSelection();
    }

    private void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _undo.CanUndo;
        RedoButton.IsEnabled = _undo.CanRedo;
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => Redo();

    private void OnUndoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Undo();
        args.Handled = true;
    }

    private void OnRedoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Redo();
        args.Handled = true;
    }

    private void OnDeleteInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_replaying)
        {
            return;
        }

        if (!DeleteSelectedObject())
        {
            DeleteSelection();
        }

        args.Handled = true;
    }

    // ---- Touch and zoom -------------------------------------------------------------------------

    private void OnDrawWithTouchChanged(object sender, RoutedEventArgs e) => ApplyInputDevices();

    private void ApplyInputDevices()
    {
        // Touch pans and zooms the ScrollViewer unless "Draw with touch" hands it to the presenter.
        CoreInputDeviceTypes devices = CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Mouse;
        if (DrawWithTouchToggle.IsChecked == true)
        {
            devices |= CoreInputDeviceTypes.Touch;
        }

        Presenter.InputDeviceTypes = devices;
    }

    private void OnResetZoomClick(object sender, RoutedEventArgs e) =>
        CanvasScroller.ChangeView(null, null, 1f);
}
