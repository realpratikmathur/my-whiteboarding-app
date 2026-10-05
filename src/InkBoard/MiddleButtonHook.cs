using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace InkBoard;

/// <summary>
/// Takes middle-button drags over the page away from the ink layer.
///
/// The InkCanvas turns a middle-button drag into ink (or erases with it) and offers no setting to stop
/// that; by the time the app is told a stroke has started it is too late. A low-level mouse hook sees the
/// button press before any window does, so when the press is over this app's page it is swallowed here and
/// reported to the app as a drag instead. Presses anywhere else, and all other mouse input, pass untouched.
/// The hook runs on its own thread so it can never slow the mouse down while the UI thread is busy.
/// </summary>
internal sealed class MiddleButtonHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_QUIT = 0x0012, WM_MOUSEMOVE = 0x0200, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    private const uint GA_ROOT = 2;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public POINT Point;
    }

    [DllImport("user32", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] private static extern int GetMessage(out MSG message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32")] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    private readonly IntPtr _window;
    private readonly HookProc _proc;          // kept alive for as long as the hook is installed
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _started = new();

    private uint _threadId;
    private IntPtr _hook;
    private int _left, _top, _right, _bottom;   // page area in window client pixels
    private volatile bool _enabled = true;
    private volatile bool _dragging;

    /// <param name="window">The app's top-level window.</param>
    public MiddleButtonHook(IntPtr window)
    {
        _window = window;
        _proc = OnMouse;
        _thread = new Thread(Run) { IsBackground = true, Name = "Middle-button hook" };
        _thread.Start();
        _started.Wait(2000);
    }

    /// <summary>Raised on the hook thread with screen pixels: drag started, moved, ended.</summary>
    public event Action<int, int>? DragStarted;

    public event Action<int, int>? DragMoved;

    public event Action? DragEnded;

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>False while something modal (a dialog) is in front of the page.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>Tells the hook where the page is inside the window, in client pixels.</summary>
    public void SetPageArea(int left, int top, int right, int bottom)
    {
        lock (_gate)
        {
            (_left, _top, _right, _bottom) = (left, top, right, bottom);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        _started.Set();
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
            // Low-level hooks are called from inside GetMessage; there is nothing to dispatch.
        }

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
        }
    }

    private bool IsOverPage(POINT screen)
    {
        if (GetAncestor(WindowFromPoint(screen), GA_ROOT) != _window)
        {
            return false;   // another app, or one of this app's menus/flyouts (they are separate windows)
        }

        var origin = default(POINT);
        ClientToScreen(_window, ref origin);
        lock (_gate)
        {
            int x = screen.X - origin.X, y = screen.Y - origin.Y;
            return x >= _left && x < _right && y >= _top && y < _bottom;
        }
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = (int)wParam;
            if (message == WM_MBUTTONDOWN && _enabled && !_dragging)
            {
                POINT point = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).Point;
                if (IsOverPage(point))
                {
                    _dragging = true;
                    DragStarted?.Invoke(point.X, point.Y);
                    return 1;   // the press never reaches the ink layer
                }
            }
            else if (_dragging && message == WM_MOUSEMOVE)
            {
                POINT point = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).Point;
                DragMoved?.Invoke(point.X, point.Y);
            }
            else if (_dragging && message == WM_MBUTTONUP)
            {
                _dragging = false;
                DragEnded?.Invoke();
                return 1;
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }
}
