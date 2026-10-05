using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Windowing;
using Windows.Foundation;

namespace InkBoard;

// Self-test scenario "realmouse": drives this window with REAL mouse input (SendInput), because what
// happens to input over the InkCanvas cannot be judged from code paths alone. The window is made
// always-on-top first so the input lands on it, and the cursor is put back afterwards. It moves the real
// cursor for a few seconds.
public sealed partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public MOUSEINPUT Mouse;
    }

    [DllImport("user32", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    private const uint MouseMove = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010,
        MiddleDown = 0x0020, MiddleUp = 0x0040,
        Absolute = 0x8000, VirtualDesk = 0x4000;

    private static void SendMouse(int screenX, int screenY, uint buttons)
    {
        // Absolute coordinates are normalised to 0..65535 across the virtual desktop.
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77), width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        var input = new INPUT
        {
            Type = 0,
            Mouse = new MOUSEINPUT
            {
                Dx = (int)Math.Round((screenX - left) * 65535.0 / (width - 1)),
                Dy = (int)Math.Round((screenY - top) * 65535.0 / (height - 1)),
                Flags = MouseMove | Absolute | VirtualDesk | buttons,
            },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Presses a button at the first point, drags through the others and releases, like a hand would.
    /// Points are relative to the top-left of the window content (so they can be on the toolbar too).
    /// </summary>
    private async Task RealDragAsync(uint down, uint up, params Point[] windowPoints)
    {
        double scale = Root.XamlRoot.RasterizationScale;
        IntPtr main = WinRT.Interop.WindowNative.GetWindowHandle(this);
        POINT Screen(Point p)
        {
            var s = new POINT { X = (int)(p.X * scale), Y = (int)(p.Y * scale) };
            ClientToScreen(main, ref s);
            return s;
        }

        POINT start = Screen(windowPoints[0]);
        SendMouse(start.X, start.Y, 0);
        await Task.Delay(120);
        SendMouse(start.X, start.Y, down);
        await Task.Delay(60);
        POINT from = start;
        foreach (Point target in windowPoints.Skip(1))
        {
            POINT to = Screen(target);
            for (int i = 1; i <= 12; i++)
            {
                SendMouse(from.X + (to.X - from.X) * i / 12, from.Y + (to.Y - from.Y) * i / 12, 0);
                await Task.Delay(15);
            }

            from = to;
        }

        await Task.Delay(60);
        SendMouse(from.X, from.Y, up);
        await Task.Delay(500);
    }

    internal async Task RunRealMouseTestAsync(StringBuilder log)
    {
        GetCursorPos(out POINT cursorBefore);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        PagesToggle.IsChecked = false;
        await Task.Delay(600);
        log.AppendLine($"middle-button hook installed: {_middleButton?.IsInstalled}");

        // Points are given relative to the visible page; the view is reset before every gesture so that
        // view point (x, y) is page point (600 + x, 500 + y) at 100%.
        Point origin = CanvasScroller.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        Point View(double x, double y) => new(origin.X + x, origin.Y + y);
        async Task ResetViewAsync(float zoom = 1f)
        {
            CanvasScroller.ChangeView(600 * zoom, 500 * zoom, zoom, disableAnimation: true);
            await Task.Delay(300);
        }

        try
        {
            // 1. Left button still inks.
            Toolbar.ActiveTool = BallpointButton;
            await ResetViewAsync();
            int strokes = StrokesOnPage();
            await RealDragAsync(LeftDown, LeftUp, View(450, 300), View(650, 300));
            log.AppendLine($"pen, LEFT drag: strokes {strokes} -> {StrokesOnPage()} (expect +1)");
            Rect drawn = _document.Container.BoundingRect;
            log.AppendLine($"    stroke landed at page x {drawn.Left:0}..{drawn.Right:0}, y {drawn.Top:0}..{drawn.Bottom:0} (expect about 1050..1250, 800)");

            // 2. Middle button pans and draws nothing.
            await ResetViewAsync();
            strokes = StrokesOnPage();
            await RealDragAsync(MiddleDown, MiddleUp, View(500, 300), View(620, 360));
            log.AppendLine($"pen, MIDDLE drag 120,60: strokes {strokes} -> {StrokesOnPage()} (expect same), {ViewState()} (expect offset 480,440)");

            // 2b. Right-button drag (what a pen barrel-button drag is) erases whole strokes, and undo restores.
            await ResetViewAsync();
            strokes = StrokesOnPage();
            await RealDragAsync(RightDown, RightUp, View(550, 250), View(555, 350));
            int afterErase = StrokesOnPage();
            Undo();
            log.AppendLine($"pen, RIGHT drag across the stroke: strokes {strokes} -> {afterErase} (expect -1), after undo {StrokesOnPage()} (expect {strokes})");

            // 3. Same while zoomed in: the page still follows the mouse one to one.
            await ResetViewAsync(2f);
            await RealDragAsync(MiddleDown, MiddleUp, View(500, 300), View(380, 240));
            log.AppendLine($"pen at 200%, MIDDLE drag -120,-60: {ViewState()} (expect offset 1320,1060), strokes {StrokesOnPage()}");

            // 4. Eraser tool: a middle drag across the stroke must not erase it.
            Toolbar.ActiveTool = Toolbar.GetToolButton(Microsoft.UI.Xaml.Controls.InkToolbarTool.Eraser);
            await ResetViewAsync();
            strokes = StrokesOnPage();
            await RealDragAsync(MiddleDown, MiddleUp, View(550, 260), View(560, 350));
            log.AppendLine($"eraser, MIDDLE drag across the stroke: strokes {strokes} -> {StrokesOnPage()} (expect same), {ViewState()} (expect offset 590,410)");

            // 5. Lasso with the left button (input through InkUnprocessedInput), then a middle drag.
            Toolbar.ActiveTool = LassoButton;
            await ResetViewAsync();
            await RealDragAsync(LeftDown, LeftUp, View(430, 270), View(670, 270), View(670, 330), View(430, 330), View(430, 272));
            log.AppendLine($"lasso, LEFT loop around the stroke: selected {_document.SelectedHandles().Count} (expect 1), " +
                $"selection box {(_selectionBounds is Rect b ? $"{b.X:0},{b.Y:0} {b.Width:0}x{b.Height:0}" : "none")}");
            await RealDragAsync(MiddleDown, MiddleUp, View(500, 380), View(620, 440));
            log.AppendLine($"lasso, MIDDLE drag: {ViewState()} (expect offset 480,440), still selected {_document.SelectedHandles().Count}");

            // 6. Lasso while zoomed: positions from the ink layer must be page coordinates.
            ClearSelection();
            await ResetViewAsync(2f);
            // At 200% with the view at page (600,500), page point (x, y) is at view ((x - 600) * 2, (y - 500) * 2).
            await RealDragAsync(LeftDown, LeftUp, View(860, 540), View(1340, 540), View(1340, 660), View(860, 660), View(860, 544));
            log.AppendLine($"lasso at 200%: selected {_document.SelectedHandles().Count} (expect 1)");
            ClearSelection();

            // 7. Point eraser with the left button cuts the stroke in two.
            Toolbar.ActiveTool = PointEraserButton;
            await ResetViewAsync();
            strokes = StrokesOnPage();
            await RealDragAsync(LeftDown, LeftUp, View(550, 260), View(550, 340));
            log.AppendLine($"point eraser, LEFT drag across the stroke: strokes {strokes} -> {StrokesOnPage()} (expect +1)");

            // 8. Text tool: a left click on empty page starts a text box there.
            Toolbar.ActiveTool = TextButton;
            await ResetViewAsync();
            await RealDragAsync(LeftDown, LeftUp, View(300, 150));
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot);
            PageObject? text = _objects.LastOrDefault();
            log.AppendLine($"text tool, LEFT click: focus on {focused?.GetType().Name} (expect TextBox), " +
                $"text box at page {text?.X:0},{text?.Y:0} (expect about 900,634)");
            await RealDragAsync(MiddleDown, MiddleUp, View(500, 300), View(620, 360));
            log.AppendLine($"text tool, MIDDLE drag: {ViewState()} (expect offset 480,440), text boxes {_objects.Count(o => o.Kind == PageObjectKind.Text)}");

            // 9. A middle press that starts on the toolbar is left alone.
            Toolbar.ActiveTool = BallpointButton;
            await ResetViewAsync();
            await RealDragAsync(MiddleDown, MiddleUp, new Point(origin.X + 500, 20), View(500, 200));
            log.AppendLine($"MIDDLE drag starting on the toolbar: {ViewState()} (expect offset 600,500)");

            // 10. And the pen tool still inks afterwards.
            strokes = StrokesOnPage();
            await RealDragAsync(LeftDown, LeftUp, View(450, 420), View(650, 420));
            log.AppendLine($"pen, LEFT drag at the end: strokes {strokes} -> {StrokesOnPage()} (expect +1)");
        }
        finally
        {
            presenter.IsAlwaysOnTop = false;
            SetCursorPos(cursorBefore.X, cursorBefore.Y);
        }
    }
}
