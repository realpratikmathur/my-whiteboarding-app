using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;

namespace InkBoard;

// Self-test scenario "viewinput": wheel zoom and middle-button pan.
//
// Part 1 calls the view code directly (proves the arithmetic and ScrollViewer calls).
// Part 2 posts real mouse messages to this app's own window, so the input travels the same route as a
// physical mouse without touching the user's cursor. A left-button drag is posted first as a control: if
// that produces an ink stroke, posted messages reach the ink input path and the rest is meaningful.
public sealed partial class MainWindow
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    private const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
        WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A;
    private const int MK_LBUTTON = 0x0001, MK_MBUTTON = 0x0010;

    private static IntPtr Pack(int low, int high) => (IntPtr)((high << 16) | (low & 0xFFFF));

    private string ViewState() =>
        $"zoom {CanvasScroller.ZoomFactor:0.###}, offset {CanvasScroller.HorizontalOffset:0},{CanvasScroller.VerticalOffset:0}";

    internal async Task RunViewInputTestAsync(StringBuilder log)
    {
        PagesToggle.IsChecked = false;
        CanvasScroller.ChangeView(600, 500, 1f, disableAnimation: true);
        await Task.Delay(400);
        log.AppendLine("start: " + ViewState());

        // ---- Part 1: the view code itself ----
        ZoomAtViewportPoint(new Point(400, 300), 120);
        await Task.Delay(200);
        log.AppendLine($"wheel up at (400,300): {ViewState()} (expect zoom 1.1, offset 700,580)");
        ZoomAtViewportPoint(new Point(400, 300), -240);
        await Task.Delay(200);
        log.AppendLine($"wheel down two notches: {ViewState()} (expect zoom 0.909)");
        CanvasScroller.ChangeView(600, 500, 1f, disableAnimation: true);
        await Task.Delay(200);
        BeginPan(new Point(1000, 800));
        PanTo(new Point(900, 750));      // mouse moved 100 left, 50 up on the page
        await Task.Delay(200);
        log.AppendLine($"pan, pointer 100 left / 50 up of the grabbed point: {ViewState()} (expect offset 700,550)");
        EndPan();

        // ---- Part 2: real mouse messages to this window ----
        CanvasScroller.ChangeView(600, 500, 1f, disableAnimation: true);
        await Task.Delay(300);
        IntPtr main = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var children = new List<(IntPtr Handle, string Class)>();
        EnumChildWindows(main, (hwnd, _) =>
        {
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, 256);
            children.Add((hwnd, name.ToString()));
            return true;
        }, IntPtr.Zero);
        log.AppendLine("child windows: " + string.Join(", ", children.Select(c => c.Class)));

        // A point in the middle of the visible page, in the coordinates of the window that hosts the XAML.
        double scale = Root.XamlRoot.RasterizationScale;
        Point canvasOrigin = CanvasScroller.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        int cx = (int)((canvasOrigin.X + 500) * scale), cy = (int)((canvasOrigin.Y + 300) * scale);

        foreach ((IntPtr target, string name) in children.Prepend((main, "main window")))
        {
            CanvasScroller.ChangeView(600, 500, 1f, disableAnimation: true);
            await Task.Delay(250);
            int strokes = StrokesOnPage();
            string before = ViewState();

            // Control: a left-button drag should ink.
            PostMessage(target, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, Pack(cx, cy));
            for (int i = 1; i <= 10; i++)
            {
                PostMessage(target, WM_MOUSEMOVE, (IntPtr)MK_LBUTTON, Pack(cx + i * 8, cy + i * 3));
                await Task.Delay(15);
            }

            PostMessage(target, WM_LBUTTONUP, IntPtr.Zero, Pack(cx + 80, cy + 30));
            await Task.Delay(500);
            int afterLeft = StrokesOnPage();

            // Wheel up one notch (wheel messages carry screen coordinates).
            var screen = new POINT { X = cx, Y = cy };
            ClientToScreen(target, ref screen);
            PostMessage(target, WM_MOUSEWHEEL, Pack(0, 120), Pack(screen.X, screen.Y));
            await Task.Delay(400);
            string afterWheel = ViewState();

            // Middle-button drag 120 px right, 60 px down.
            CanvasScroller.ChangeView(600, 500, 1f, disableAnimation: true);
            await Task.Delay(250);
            PostMessage(target, WM_MBUTTONDOWN, (IntPtr)MK_MBUTTON, Pack(cx, cy));
            for (int i = 1; i <= 12; i++)
            {
                PostMessage(target, WM_MOUSEMOVE, (IntPtr)MK_MBUTTON, Pack(cx + i * 10, cy + i * 5));
                await Task.Delay(15);
            }

            PostMessage(target, WM_MBUTTONUP, IntPtr.Zero, Pack(cx + 120, cy + 60));
            await Task.Delay(500);

            log.AppendLine($"[{name}] left drag: strokes {strokes} -> {afterLeft}; wheel up: {before} -> {afterWheel}; " +
                $"middle drag 120,60: -> {ViewState()} (pan works if offset is 480,440), strokes now {StrokesOnPage()}");
        }
    }
}
