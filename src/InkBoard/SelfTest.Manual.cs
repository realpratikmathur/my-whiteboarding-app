using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenario "replay-manual": "Follow: off" with real input while a replay is PLAYING.
// Mouse wheel and middle-button drag are sent with SendInput (the real cursor moves for a few seconds);
// one-finger pan and two-finger pinch are sent with Windows touch injection, as a finger on the screen
// would. Checks that the view moves the way the presenter moves it, that the replay does not move it back,
// and that the replay carries on, also off screen.
public sealed partial class MainWindow
{
    // ---- Touch injection (user32 InjectTouchInput) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint PointerType, PointerId, FrameId, PointerFlags;
        public IntPtr SourceDevice, HwndTarget;
        public POINT PixelLocation, HimetricLocation, PixelLocationRaw, HimetricLocationRaw;
        public uint Time, HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO PointerInfo;
        public uint TouchFlags, TouchMask;
        public RECT Contact, ContactRaw;
        public uint Orientation, Pressure;
    }

    [DllImport("user32", SetLastError = true)] private static extern bool InitializeTouchInjection(uint maxCount, uint feedbackMode);
    [DllImport("user32", SetLastError = true)] private static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);

    private const uint PointerDown = 0x10000 | 0x2 | 0x4, PointerUpdate = 0x20000 | 0x2 | 0x4, PointerUp = 0x40000;
    private const uint MouseWheel = 0x0800;

    private static POINTER_TOUCH_INFO Contact(uint id, int x, int y, uint flags) => new()
    {
        PointerInfo = new POINTER_INFO { PointerType = 2, PointerId = id, PointerFlags = flags, PixelLocation = new POINT { X = x, Y = y } },
        TouchMask = 0x1 | 0x4,   // contact area and pressure
        Contact = new RECT { Left = x - 4, Top = y - 4, Right = x + 4, Bottom = y + 4 },
        Pressure = 512,
    };

    /// <summary>Moves the given fingers in a straight line from their start to their end positions.</summary>
    private static async Task<string> TouchGestureAsync((POINT From, POINT To)[] fingers)
    {
        var frame = new POINTER_TOUCH_INFO[fingers.Length];
        bool Send(uint flags, double t)
        {
            for (int f = 0; f < fingers.Length; f++)
            {
                (POINT a, POINT b) = fingers[f];
                frame[f] = Contact((uint)f, (int)(a.X + (b.X - a.X) * t), (int)(a.Y + (b.Y - a.Y) * t), flags);
            }

            return InjectTouchInput((uint)frame.Length, frame);
        }

        if (!Send(PointerDown, 0))
        {
            return $"touch injection refused (error {Marshal.GetLastWin32Error()})";
        }

        for (int i = 1; i <= 20; i++)
        {
            await Task.Delay(16);
            Send(PointerUpdate, i / 20.0);
        }

        await Task.Delay(16);
        Send(PointerUpdate, 1);   // hold still a moment so the pan ends without a fling
        await Task.Delay(120);
        Send(PointerUpdate, 1);
        Send(PointerUp, 1);
        return "ok";
    }

    private static void SendWheel(int screenX, int screenY, int delta)
    {
        SendMouse(screenX, screenY, 0);
        var input = new INPUT { Type = 0, Mouse = new MOUSEINPUT { MouseData = unchecked((uint)delta), Flags = MouseWheel } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    internal async Task RunReplayManualTestAsync(StringBuilder log)
    {
        GetCursorPos(out POINT cursorBefore);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        Activate();
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetForegroundWindow(hwnd);
        PagesToggle.IsChecked = false;
        log.AppendLine($"touch injection available: {InitializeTouchInjection(2, 3)}");

        try
        {
            // A lesson whose last line is far below the first screen.
            InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
            pen.Size = new Size(5, 5);
            StartRecording();
            await DrawAsync(pen, new Point(100, 150), new Point(600, 160), 2500);
            await DrawAsync(pen, new Point(100, 250), new Point(600, 260), 2500);
            await DrawAsync(pen, new Point(100, 1800), new Point(600, 1810), 2500);
            StopRecording();
            await SaveCurrentPageAsync();

            CanvasScroller.ChangeView(0, 60, 1f, disableAnimation: true);
            await Task.Delay(300);
            FollowBox.SelectedIndex = 2;
            StartBox.SelectedIndex = 0;
            SpeedBox.SelectedIndex = 1;
            await EnterReplayAsync();
            await Task.Delay(400);
            log.AppendLine($"replay opened with Follow: off: {ViewState()} (expect offset 0,60 - the view stays where it was)");

            // Screen point in the middle of the visible page.
            double scale = Root.XamlRoot.RasterizationScale;
            Point origin = CanvasScroller.TransformToVisual(Root).TransformPoint(new Point(0, 0));
            POINT Screen(double x, double y)
            {
                var p = new POINT { X = (int)((origin.X + x) * scale), Y = (int)((origin.Y + y) * scale) };
                ClientToScreen(hwnd, ref p);
                return p;
            }

            SetReplayPlaying(true);
            await Task.Delay(400);

            // 1. Wheel up = zoom in, while the replay is playing.
            POINT centre = Screen(400, 300);
            SendWheel(centre.X, centre.Y, 120);
            await Task.Delay(500);
            string afterWheel = ViewState();
            await Task.Delay(1000);
            log.AppendLine($"wheel up during replay: {afterWheel} (expect zoom 1.1); one second later {ViewState()} (expect unchanged); " +
                $"strokes on page {RealStrokesOnPage()}, clock {_clock.Position:0} ms, playing {_clock.IsPlaying}");

            // 2. Middle-button drag 150 left / 100 up = the page moves with it.
            string beforeDrag = ViewState();
            await RealDragAsync(MiddleDown, MiddleUp, View(400, 300), View(250, 200));
            string afterDrag = ViewState();
            await Task.Delay(1000);
            log.AppendLine($"middle drag -150,-100 during replay: {beforeDrag} -> {afterDrag} (expect offset +150,+100); one second later {ViewState()}");

            // 3. One finger drags the page up by 250 px.
            string beforeTouch = ViewState();
            POINT from = Screen(500, 450), to = Screen(500, 200);
            string touch = await TouchGestureAsync([(from, to)]);
            await Task.Delay(1200);
            log.AppendLine($"one-finger pan up 250 px during replay ({touch}): {beforeTouch} -> {ViewState()} (expect vertical offset about +250)");

            // 4. Two fingers spread apart = zoom in.
            string beforePinch = ViewState();
            string pinch = await TouchGestureAsync(
            [
                (Screen(450, 300), Screen(350, 300)),
                (Screen(550, 300), Screen(650, 300)),
            ]);
            await Task.Delay(1200);
            log.AppendLine($"two-finger pinch out during replay ({pinch}): {beforePinch} -> {ViewState()} (expect a larger zoom)");

            // 5. The replay carries on to the end, also where the view is not looking.
            string viewBeforeEnd = ViewState();
            SpeedBox.SelectedIndex = 4;
            while (!_clock.AtEnd)
            {
                await Task.Delay(100);
            }

            log.AppendLine($"end of replay: strokes {RealStrokesOnPage()} (expect 3, the last one drawn off screen); " +
                $"view {ViewState()} (expect unchanged from {viewBeforeEnd})");
            await ExitReplayAsync();
        }
        finally
        {
            presenter.IsAlwaysOnTop = false;
            SetCursorPos(cursorBefore.X, cursorBefore.Y);
        }

        Point View(double x, double y)
        {
            Point o = CanvasScroller.TransformToVisual(Root).TransformPoint(new Point(0, 0));
            return new Point(o.X + x, o.Y + y);
        }
    }
}
