using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Extra self-test scenarios: InkBoard.exe --selftest <log> <scenario> [argument]
public sealed partial class MainWindow
{
    /// <summary>Endless page: ink far down the page, then scroll there.</summary>
    internal async Task RunBigPageTestAsync(StringBuilder log)
    {
        log.AppendLine($"page at start {PageSurface.Width}x{PageSurface.Height}");

        InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
        pen.Size = new Size(6, 6);
        InkDrawingAttributes pencil = InkDrawingAttributes.CreateForPencil();
        pencil.Size = new Size(10, 10);
        int a = _document.AddNew(SelfTest.MakeStroke(pen, new Point(200, 58000), new Point(900, 58100)));
        int b = _document.AddNew(SelfTest.MakeStroke(pencil, new Point(200, 58200), new Point(900, 58150)));
        _undo.Push(new AddStrokesStep([a, b]));
        await Task.Delay(500);
        log.AppendLine($"after ink at y=58000: page {PageSurface.Width}x{PageSurface.Height}, " +
            $"InkCanvas actual {InkSurface.ActualWidth}x{InkSurface.ActualHeight}");

        CanvasScroller.ChangeView(0, 57800, 1f, disableAnimation: true);
        await Task.Delay(1500);
        log.AppendLine($"scrolled to {CanvasScroller.VerticalOffset:0} (extent {CanvasScroller.ExtentHeight:0}); " +
            $"page now {PageSurface.Width}x{PageSurface.Height}");
        log.AppendLine($"strokes {_document.Container.GetStrokes().Count}, bounds {_document.Container.BoundingRect}");

        // Scrolling to the bottom must keep extending the page.
        for (int i = 0; i < 5; i++)
        {
            CanvasScroller.ChangeView(0, CanvasScroller.ScrollableHeight, 1f, disableAnimation: true);
            await Task.Delay(300);
        }

        log.AppendLine($"after scrolling to the bottom 5 times: page height {PageSurface.Height}");
        CanvasScroller.ChangeView(0, 57800, 1f, disableAnimation: true);
        await Task.Delay(1500);
    }
}
