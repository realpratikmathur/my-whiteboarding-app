using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenario "picture-after": a picture is inserted and moved, then the presenter writes, records and
// replays; after every step a REAL mouse drag with the select tool must still move the picture.
// The real cursor moves over this window for some seconds.
public sealed partial class MainWindow
{
    internal async Task RunPictureAfterTestAsync(StringBuilder log)
    {
        GetCursorPos(out POINT cursorBefore);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        PagesToggle.IsChecked = false;
        string folder = Path.GetDirectoryName(SelfTest.LogPath)!;
        Point At(double pageX, double pageY) => PageSurface.TransformToVisual(Root).TransformPoint(new Point(pageX, pageY));
        PageObject? Picture() => _objects.FirstOrDefault(o => o.Kind == PageObjectKind.Image);

        // Select tool, then a real drag from the middle of the picture by +40,+20. Reports whether it moved.
        async Task<string> TryDragAsync(string when)
        {
            Toolbar.ActiveTool = TextButton;
            CanvasScroller.ChangeView(0, 0, 1f, disableAnimation: true);
            await Task.Delay(400);
            if (Picture() is not PageObject p)
            {
                return $"{when}: NO PICTURE ON THE PAGE";
            }

            (double x, double y) = (p.X, p.Y);
            await RealDragAsync(LeftDown, LeftUp, At(x + p.Width / 2, y + p.Height / 2), At(x + p.Width / 2 + 40, y + p.Height / 2 + 20));
            PageObject after = Picture()!;
            bool moved = Math.Abs(after.X - x - 40) < 1 && Math.Abs(after.Y - y - 20) < 1;
            string line = $"{when}: drag {(moved ? "MOVED it" : "did NOT move it")} ({x:0},{y:0} -> {after.X:0},{after.Y:0}); " +
                $"selected {(_selectedObject is null ? "nothing" : _selectedObject.Kind.ToString())}, presenter mode {Presenter.InputProcessingConfiguration.Mode}, " +
                $"input enabled {Presenter.IsInputEnabled}, objects {_objects.Count}, same object as before {ReferenceEquals(after, p)}";
            log.AppendLine(line);
            return line;
        }

        try
        {
            InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
            pen.Size = new Size(10, 10);
            int stroke = _document.AddNew(SelfTest.MakeStroke(pen, new Point(0, 0), new Point(260, 110)));
            string png = Path.Combine(folder, "picture-after.png");
            await ExportPngAsync(png);
            _document.Remove([stroke]);

            await InsertFilesAsync([png], new Point(200, 150));
            await Task.Delay(300);
            await TryDragAsync("1. right after inserting");

            // 2. Write with the pen, across the picture and elsewhere.
            Toolbar.ActiveTool = BallpointButton;
            await Task.Delay(300);
            PageObject p = Picture()!;
            await RealDragAsync(LeftDown, LeftUp, At(p.X - 30, p.Y + 40), At(p.X + p.Width + 30, p.Y + 60));
            await RealDragAsync(LeftDown, LeftUp, At(700, 400), At(900, 420));
            log.AppendLine($"   pen strokes on the page: {StrokesOnPage()}");
            await TryDragAsync("2. after writing over it with the pen");

            // 3. Record some handwriting, replay to the end, exit.
            Toolbar.ActiveTool = BallpointButton;
            await Task.Delay(300);
            StartRecording();
            await RealDragAsync(LeftDown, LeftUp, At(150, 500), At(450, 520));
            await RealDragAsync(LeftDown, LeftUp, At(150, 560), At(450, 580));
            StopRecording();
            await SaveCurrentPageAsync();
            await EnterReplayAsync();
            SpeedBox.SelectedIndex = 4;
            SetReplayPlaying(true);
            while (!_clock.AtEnd)
            {
                await Task.Delay(50);
            }

            await ExitReplayAsync();
            await TryDragAsync("3. after recording and replaying");

            // 4. Replay again with a rewind in the middle, exit.
            await EnterReplayAsync();
            SeekReplay(_clock.Length);
            SeekReplay(_clock.Length / 3);
            await Task.Delay(300);
            await ExitReplayAsync();
            await TryDragAsync("4. after a replay with a rewind");

            // 5. Ghost-page replay, exit.
            StartBox.SelectedIndex = 1;
            await EnterReplayAsync();
            SeekReplay(_clock.Length);
            await ExitReplayAsync();
            StartBox.SelectedIndex = 0;
            await TryDragAsync("5. after a ghost-page replay");

            // 6. Another page and back.
            NotebookPage first = _currentPage!;
            await AddPageAsync("Other");
            await OpenPageAsync(first);
            await TryDragAsync("6. after switching to another page and back");

            // 7. The lasso takes in the picture together with ink, and moves them together.
            Toolbar.ActiveTool = BallpointButton;
            await Task.Delay(300);
            PageObject pic = Picture()!;
            await RealDragAsync(LeftDown, LeftUp, At(pic.X + 20, pic.Bottom + 30), At(pic.X + 200, pic.Bottom + 35));   // a stroke under it
            Toolbar.ActiveTool = LassoButton;
            await Task.Delay(300);
            (double px, double py) = (pic.X, pic.Y);
            double strokesTop = _document.Container.BoundingRect.Y;
            await RealDragAsync(LeftDown, LeftUp,
                At(px - 30, py - 30), At(pic.Right + 30, py - 30), At(pic.Right + 30, pic.Bottom + 60), At(px - 30, pic.Bottom + 60), At(px - 30, py - 28));
            log.AppendLine($"7. lasso around the picture and the stroke under it: pictures taken in {_lassoObjects.Count} (expect 1), strokes selected {_document.SelectedHandles().Count} (expect 1)");
            Rect inkBefore = _document.Container.BoundingRect;
            await RealDragAsync(LeftDown, LeftUp, At(pic.X + pic.Width / 2, pic.Y + pic.Height / 2), At(pic.X + pic.Width / 2 + 100, pic.Y + pic.Height / 2 + 50));
            log.AppendLine($"   dragged the selection +100,+50: picture {px:0},{py:0} -> {pic.X:0},{pic.Y:0}; selected ink moved too: {_document.SelectedHandles().Count == 1}");
            Undo();
            log.AppendLine($"   one undo: picture back at {pic.X:0},{pic.Y:0} (expect {px:0},{py:0})");

            // 8. Lasso just the picture, Delete, undo.
            ClearSelection();
            await RealDragAsync(LeftDown, LeftUp,
                At(px - 20, py - 20), At(pic.Right + 20, py - 20), At(pic.Right + 20, pic.Bottom + 10), At(px - 20, pic.Bottom + 10), At(px - 20, py - 18));
            log.AppendLine($"8. lasso around the picture only: pictures taken in {_lassoObjects.Count}, strokes {_document.SelectedHandles().Count}");
            SendKeyInput(1, [new KEYINPUT { Type = 1, VirtualKey = 0x2E }], 40);
            SendKeyInput(1, [new KEYINPUT { Type = 1, VirtualKey = 0x2E, Flags = 0x0002 }], 40);
            await Task.Delay(400);
            log.AppendLine($"   Delete key: pictures on the page {_objects.Count(o => o.Kind == PageObjectKind.Image)} (expect 0)");
            Undo();
            log.AppendLine($"   undo: pictures on the page {_objects.Count(o => o.Kind == PageObjectKind.Image)} (expect 1)");
            await TryDragAsync("9. select tool again after all that");
            log.AppendLine($"   font bar stayed visible while the picture was selected: {TextFormatBar.Visibility == Microsoft.UI.Xaml.Visibility.Visible}; font box enabled {FontBox.IsEnabled} (expect False)");
        }
        finally
        {
            presenter.IsAlwaysOnTop = false;
            SetCursorPos(cursorBefore.X, cursorBefore.Y);
        }
    }
}
