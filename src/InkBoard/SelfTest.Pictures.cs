using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenario "pictures": inserting a picture and a PDF, then moving and resizing them with REAL
// mouse drags (SendInput). The real cursor moves over this window for a few seconds.
public sealed partial class MainWindow
{
    private static string RectText(PageObject? o) => o is null ? "none" : $"{o.X:0},{o.Y:0} {o.Width:0}x{o.Height:0}";

    internal async Task RunPicturesTestAsync(StringBuilder log)
    {
        GetCursorPos(out POINT cursorBefore);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        PagesToggle.IsChecked = false;
        string folder = Path.GetDirectoryName(SelfTest.LogPath)!;

        // Page point -> point in the window content, for RealDragAsync.
        Point At(double pageX, double pageY) => PageSurface.TransformToVisual(Root).TransformPoint(new Point(pageX, pageY));

        try
        {
            // A 300 x 150 picture to insert: export one stroke as PNG.
            InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
            pen.Size = new Size(10, 10);
            pen.Color = Microsoft.UI.Colors.DarkGreen;
            int stroke = _document.AddNew(SelfTest.MakeStroke(pen, new Point(0, 0), new Point(260, 110)));
            string png = Path.Combine(folder, "pictures-source.png");
            await ExportPngAsync(png);
            _document.Remove([stroke]);
            CanvasScroller.ChangeView(0, 0, 1f, disableAnimation: true);
            Toolbar.ActiveTool = BallpointButton;
            await Task.Delay(400);

            // 1. Insert through the same path as File > Insert picture.
            log.AppendLine("insert: " + await InsertFilesAsync([png], new Point(200, 150)));
            await Task.Delay(400);
            PageObject picture = _objects.Single(o => o.Kind == PageObjectKind.Image);
            log.AppendLine($"after insert: tool {(ReferenceEquals(Toolbar.ActiveTool, TextButton) ? "select (A)" : "other")}, " +
                $"selected {(ReferenceEquals(_selectedObject, picture) ? "the picture" : "nothing")}, " +
                $"handles shown {_handleViews.Values.Count(v => v.Visibility == Visibility.Visible)}, text font bar shown {TextFormatBar.Visibility == Visibility.Visible} with its controls enabled {FontBox.IsEnabled} (expect True, False: it stays in place, greyed out); picture {RectText(picture)}");
            await SelfTest.SnapshotAsync("pictures-selected");
            (double x0, double y0, double w0, double h0) = (picture.X, picture.Y, picture.Width, picture.Height);

            // 2. Drag from inside the picture.
            await RealDragAsync(LeftDown, LeftUp, At(x0 + w0 / 2, y0 + h0 / 2), At(x0 + w0 / 2 + 120, y0 + h0 / 2 + 60));
            log.AppendLine($"drag the picture +120,+60: {RectText(picture)} (expect {x0 + 120:0},{y0 + 60:0} {w0:0}x{h0:0}); strokes drawn {StrokesOnPage()} (expect 0)");

            // 3. Bottom-right corner: proportional, top-left stays.
            (double x, double y, double w, double h) = (picture.X, picture.Y, picture.Width, picture.Height);
            await RealDragAsync(LeftDown, LeftUp, At(x + w, y + h), At(x + w + 150, y + h + 10));
            log.AppendLine($"drag bottom-right corner +150: {RectText(picture)} (expect {x:0},{y:0} {w + 150:0}x{h * (w + 150) / w:0})");

            // 4. Top-left corner: bottom-right stays.
            (x, y, w, h) = (picture.X, picture.Y, picture.Width, picture.Height);
            await RealDragAsync(LeftDown, LeftUp, At(x, y), At(x + 90, y + 20));
            log.AppendLine($"drag top-left corner +90: {RectText(picture)}; bottom-right {picture.Right:0},{picture.Bottom:0} (expect {x + w:0},{y + h:0}); " +
                $"proportions {picture.Width / picture.Height:0.00} (expect {w0 / h0:0.00})");

            // 5. Right edge: width only.
            (x, y, w, h) = (picture.X, picture.Y, picture.Width, picture.Height);
            await RealDragAsync(LeftDown, LeftUp, At(x + w, y + h / 2), At(x + w + 80, y + h / 2 + 30));
            log.AppendLine($"drag right edge +80: {RectText(picture)} (expect {x:0},{y:0} {w + 80:0}x{h:0})");

            // 6. At 200% the handles are still easy to hit and the picture follows the mouse.
            CanvasScroller.ChangeView(picture.X * 2 - 100, picture.Y * 2 - 100, 2f, disableAnimation: true);
            await Task.Delay(500);
            (x, y, w, h) = (picture.X, picture.Y, picture.Width, picture.Height);
            double handleOnScreen = _handleViews[ResizeHandle.BottomRight].Width * CanvasScroller.ZoomFactor;
            await RealDragAsync(LeftDown, LeftUp, At(x + w, y + h / 2), At(x + w - 40, y + h / 2));   // 80 screen px left
            log.AppendLine($"200%: handle {handleOnScreen:0} px on screen (expect 10); right edge -40 page units: width {picture.Width:0} (expect {w - 40:0})");
            await RealDragAsync(LeftDown, LeftUp, At(x + 20, y + 20), At(x + 70, y + 20));
            log.AppendLine($"200%: drag +50 page units: x {picture.X:0} (expect {x + 50:0})");
            await SelfTest.SnapshotAsync("pictures-zoomed");
            CanvasScroller.ChangeView(0, 0, 1f, disableAnimation: true);
            await Task.Delay(400);

            // 7. Undo walks back through the moves and resizes.
            for (int i = 0; i < 6; i++)   // six changes since the insert
            {
                Undo();
            }

            log.AppendLine($"after 6 undos: {RectText(picture)} (expect back at {x0:0},{y0:0} {w0:0}x{h0:0}), still on the page {_objects.Contains(picture)}");

            // 8. A 2-page PDF: first page selected, second page can be picked and moved.
            Toolbar.ActiveTool = BallpointButton;
            string pdf = Path.Combine(folder, "pictures.pdf");
            File.WriteAllText(pdf, SelfTest.MinimalPdf(["Page one", "Page two"]), Encoding.ASCII);
            log.AppendLine("insert: " + await InsertFilesAsync([pdf], new Point(700, 100)));
            await Task.Delay(400);
            PageObject[] pages = _objects.Where(o => o.Kind == PageObjectKind.Image && o.X >= 700).OrderBy(o => o.Y).ToArray();
            log.AppendLine($"PDF: {pages.Length} pages; selected first page {ReferenceEquals(_selectedObject, pages[0])}");
            PageObject second = pages[1];
            (x, y) = (second.X, second.Y);
            CanvasScroller.ChangeView(0, (second.Y - 100) * 1, 1f, disableAnimation: true);
            await Task.Delay(400);
            await RealDragAsync(LeftDown, LeftUp, At(x + 200, y + 200), At(x + 300, y + 230));
            log.AppendLine($"click-drag the second PDF page +100,+30: {RectText(second)} (expect {x + 100:0},{y + 30:0}); selected it {ReferenceEquals(_selectedObject, second)}");

            // 9. Moves and resizes are recorded and replay.
            CanvasScroller.ChangeView(0, 0, 1f, disableAnimation: true);
            await Task.Delay(300);
            StartRecording();
            log.AppendLine($"recording started: {_isRecording}, events {_recording.Events.Count}, tool {(ReferenceEquals(Toolbar.ActiveTool, TextButton) ? "select (A)" : Toolbar.ActiveTool?.ToolKind.ToString())}, " +
                $"presenter mode {Presenter.InputProcessingConfiguration.Mode}, input enabled {Presenter.IsInputEnabled}, focus {FocusName()}");
            (x, y, w, h) = (picture.X, picture.Y, picture.Width, picture.Height);
            await RealDragAsync(LeftDown, LeftUp, At(x + 30, y + 30), At(x + 30, y + 130));
            log.AppendLine($"    after drag 1: {RectText(picture)}, selected {RectText(_selectedObject)}, strokes {StrokesOnPage()}");
            await RealDragAsync(LeftDown, LeftUp, At(picture.Right, picture.Bottom), At(picture.Right + 60, picture.Bottom + 60));
            log.AppendLine($"    after drag 2: {RectText(picture)}, events {_recording.Events.Count}");
            StopRecording();
            string recorded = RectText(picture);
            await SaveCurrentPageAsync();
            await EnterReplayAsync();
            SpeedBox.SelectedIndex = 4;
            SetReplayPlaying(true);
            while (!_clock.AtEnd)
            {
                await Task.Delay(50);
            }

            PageObject? replayed = _replayObjects.Values.FirstOrDefault(o => o.Asset == picture.Asset);
            log.AppendLine($"replay ends with the picture at {RectText(replayed)} (expect {recorded})");
            await ExitReplayAsync();
        }
        finally
        {
            presenter.IsAlwaysOnTop = false;
            SetCursorPos(cursorBefore.X, cursorBefore.Y);
        }
    }
}
