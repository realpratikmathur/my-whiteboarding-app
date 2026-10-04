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
            DispatcherQueue.TryEnqueue(() => _undo.Push(new AddStrokesStep(_document.Track(strokes))));
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
        Presenter.UnprocessedInput.PointerPressed += (_, args) => RouteToolInput(args, OnToolPressed);
        Presenter.UnprocessedInput.PointerMoved += (_, args) => RouteToolInput(args, OnToolMoved);
        Presenter.UnprocessedInput.PointerReleased += (_, args) => RouteToolInput(args, OnToolReleased);
        Presenter.UnprocessedInput.PointerLost += (_, args) => RouteToolInput(args, OnToolReleased);

        CanvasScroller.ViewChanged += (_, _) =>
        {
            ZoomText.Text = $"{CanvasScroller.ZoomFactor * 100:0}%";
            UpdatePageExtent();
        };
        CanvasScroller.SizeChanged += (_, _) => UpdatePageExtent();
        _undo.Changed += (_, _) => OnPageContentChanged();
        ApplyInputDevices();
    }

    // ---- Endless page ---------------------------------------------------------------------------

    private Rect _contentBounds;

    private void OnPageContentChanged()
    {
        _contentBounds = _document.Container.BoundingRect;
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

    private void RouteToolInput(PointerEventArgs args, System.Action<Point> handler)
    {
        Point position = args.CurrentPoint.Position;
        DispatcherQueue.TryEnqueue(() => handler(position));
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
        if (!LassoActive)
        {
            ClearSelection();
        }
    }

    // ---- Tool input -----------------------------------------------------------------------------

    private void OnToolPressed(Point position)
    {
        _toolPointerDown = true;
        _lastToolPoint = position;

        if (LassoActive)
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

        if (_draggingSelection)
        {
            var delta = new Point(position.X - _lastToolPoint.X, position.Y - _lastToolPoint.Y);
            ShowSelection(_document.Container.MoveSelected(delta));
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

        if (_draggingSelection)
        {
            _draggingSelection = false;
            if (_dragTotal != default)
            {
                _undo.Push(new MoveStrokesStep(_document.SelectedHandles(), _dragTotal.X, _dragTotal.Y));
            }
        }
        else if (_lassoPoints is not null)
        {
            Rect bounds = _document.Container.SelectWithPolyLine(_lassoPoints);
            _lassoPoints = null;
            LassoLine.Visibility = Visibility.Collapsed;
            ShowSelection(bounds);
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

    private void ShowSelection(Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
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
        _draggingSelection = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        LassoLine.Visibility = Visibility.Collapsed;
    }

    private void DeleteSelection()
    {
        IReadOnlyList<int> selected = _document.SelectedHandles();
        if (selected.Count == 0)
        {
            return;
        }

        _document.Remove(selected);
        _undo.Push(new RemoveStrokesStep(selected));
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
        ClearSelection();
        _undo.Undo();
        _document.ClearSelection();
    }

    private void Redo()
    {
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
        DeleteSelection();
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
