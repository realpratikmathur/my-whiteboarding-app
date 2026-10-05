using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace InkBoard;

// Self-test scenario "textformat": fonts, sizes, bold and italic for text boxes, driven with REAL mouse
// clicks and key presses where the input path matters (clicking the page, clicking the format bar, typing).
// It moves the real cursor and types into this window for a few seconds.
public sealed partial class MainWindow
{
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct KEYINPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
    }

    [DllImport("user32", EntryPoint = "SendInput", SetLastError = true)]
    private static extern uint SendKeyInput(uint count, KEYINPUT[] inputs, int size);

    private static async Task TypeRealAsync(string text)
    {
        foreach (char c in text)
        {
            const uint Unicode = 0x0004, KeyUp = 0x0002;
            SendKeyInput(1, [new KEYINPUT { Type = 1, Scan = c, Flags = Unicode }], 40);
            SendKeyInput(1, [new KEYINPUT { Type = 1, Scan = c, Flags = Unicode | KeyUp }], 40);
            await Task.Delay(40);
        }

        await Task.Delay(250);
    }

    private Point CentreOf(FrameworkElement element) =>
        element.TransformToVisual(Root).TransformPoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    private string FocusName() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot)?.GetType().Name ?? "nothing";

    private static string Describe(PageObject? o) =>
        o is null ? "none" : $"\"{o.Text.Replace("\r", "\\r")}\" {o.FontFamily} {o.FontSize:0.#}{(o.Bold ? " bold" : "")}{(o.Italic ? " italic" : "")}";

    private string DescribeView(PageObject o) =>
        _objectViews.TryGetValue(o, out FrameworkElement? view) && view is TextBox box
            ? $"{box.FontFamily.Source} {box.FontSize:0.#}{(box.FontWeight.Weight >= 700 ? " bold" : "")}{(box.FontStyle == Windows.UI.Text.FontStyle.Italic ? " italic" : "")}"
            : "no text box";

    internal async Task RunTextFormatTestAsync(StringBuilder log)
    {
        GetCursorPos(out POINT cursorBefore);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        PagesToggle.IsChecked = false;
        await Task.Delay(600);

        try
        {
            log.AppendLine($"fonts offered: {(FontBox.ItemsSource as System.Collections.IList)?.Count}; sizes: {string.Join(" ", (FontSizeBox.ItemsSource as System.Collections.Generic.List<string>)!)}");
            log.AppendLine($"format bar visible with the pen tool: {TextFormatBar.Visibility == Visibility.Visible} (expect False)");
            Toolbar.ActiveTool = TextButton;
            await Task.Delay(400);
            log.AppendLine($"format bar visible with the text tool: {TextFormatBar.Visibility == Visibility.Visible} (expect True)");
            Point origin = CanvasScroller.TransformToVisual(Root).TransformPoint(new Point(0, 0));
            Point View(double x, double y) => new(origin.X + x, origin.Y + y);

            // 1. Click the page, then click Bold BEFORE typing: the empty box must survive and typing must continue.
            await RealDragAsync(LeftDown, LeftUp, View(200, 120));
            log.AppendLine($"clicked the page: {_objects.Count} text box, focus on {FocusName()}");
            await RealDragAsync(LeftDown, LeftUp, CentreOf(BoldToggle));
            await Task.Delay(300);
            PageObject? first = _objects.FirstOrDefault();
            log.AppendLine($"clicked Bold with the box still empty: boxes {_objects.Count} (expect 1), {Describe(first)}, focus back on {FocusName()} (expect TextBox)");
            await TypeRealAsync("Heading");
            log.AppendLine($"typed with the real keyboard: {Describe(first)} (expect \"Heading\" ... bold)");

            // 2. Font and size from the bar while the box is selected.
            FontBox.SelectedItem = "Georgia";
            await Task.Delay(200);
            FontSizeBox.SelectedItem = "36";
            await Task.Delay(300);
            log.AppendLine($"chose Georgia and 36: model {Describe(first)}; on screen {DescribeView(first!)}; focus {FocusName()}");
            ApplyFormatBar("30.5");   // a size typed into the box
            await Task.Delay(200);
            log.AppendLine($"typed size 30.5: on screen {DescribeView(first!)}; size box shows \"{FontSizeBox.Text}\"");

            // 3. Click elsewhere: a new box takes the current style; typing there.
            await RealDragAsync(LeftDown, LeftUp, View(200, 320));
            await TypeRealAsync("Second");
            PageObject? second = _objects.LastOrDefault();
            log.AppendLine($"second box: {Describe(second)} (expect Georgia 30.5 bold); boxes {_objects.Count} (expect 2)");

            // 4. Selecting the first box again shows its style on the bar; change only the second.
            ItalicToggle.IsChecked = true;
            await Task.Delay(200);
            BoldToggle.IsChecked = false;
            FontSizeBox.SelectedItem = "14";
            FontBox.SelectedItem = "Consolas";
            await Task.Delay(300);
            log.AppendLine($"restyled second: {Describe(second)}; first untouched: {Describe(first)}");
            await RealDragAsync(LeftDown, LeftUp, new Point(origin.X + first!.X + 20, origin.Y + first.Y + 20));
            await Task.Delay(300);
            log.AppendLine($"clicked the first box: bar shows {FontBox.SelectedItem} {FontSizeBox.Text} bold {BoldToggle.IsChecked} italic {ItalicToggle.IsChecked} (expect Georgia 30.5 True False)");

            // 5. A click on empty page that is not typed into leaves nothing behind.
            await RealDragAsync(LeftDown, LeftUp, View(700, 500));
            Toolbar.ActiveTool = BallpointButton;
            await Task.Delay(400);
            log.AppendLine($"stray click then pen tool: boxes {_objects.Count} (expect 2)");

            // 6. Undo and redo step through style changes of an existing box.
            Toolbar.ActiveTool = TextButton;
            SelectObject(first);
            FontSizeBox.SelectedItem = "48";
            ItalicToggle.IsChecked = true;
            await Task.Delay(200);
            string changed = Describe(first);
            Undo();
            Undo();
            string undone = Describe(first) + " / on screen " + DescribeView(first);
            Redo();
            Redo();
            log.AppendLine($"style undo: {changed} -> {undone} (expect Georgia 30.5 bold) -> redo {Describe(first)}");
            Undo();
            Undo();
            Toolbar.ActiveTool = BallpointButton;

            // 7. Style changes are recorded and replayed.
            Toolbar.ActiveTool = TextButton;
            StartRecording();
            await Task.Delay(300);
            SelectObject(first);
            BoldToggle.IsChecked = false;
            FontSizeBox.SelectedItem = "72";
            await Task.Delay(300);
            StopRecording();
            log.AppendLine($"recorded style events: {_recording.Events.Count(e => e.Kind == RecordedEventKind.ObjectStyle)}");
            await SaveCurrentPageAsync();
            await EnterReplayAsync();
            SpeedBox.SelectedIndex = 4;
            SetReplayPlaying(true);
            while (!_clock.AtEnd)
            {
                await Task.Delay(50);
            }

            PageObject? replayed = _replayObjects.Values.FirstOrDefault(o => o.Text == "Heading");
            log.AppendLine($"end of replay: {Describe(replayed)}; on screen {(replayed is null ? "-" : DescribeView(replayed))} (expect Georgia 72, not bold)");
            await ExitReplayAsync();

            // 8. Saved with the page.
            NotebookPage page = _currentPage!;
            await AddPageAsync("Other");
            await OpenPageAsync(page);
            log.AppendLine("after reloading the page: " + string.Join(" | ", _objects.Select(o => Describe(o) + " / on screen " + DescribeView(o))));
            Toolbar.ActiveTool = TextButton;
            await Task.Delay(500);
        }
        finally
        {
            presenter.IsAlwaysOnTop = false;
            SetCursorPos(cursorBefore.X, cursorBefore.Y);
        }
    }
}
