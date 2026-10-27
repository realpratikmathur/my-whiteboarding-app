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
    /// <summary>Text box, picture and PDF pages under ink.</summary>
    internal async Task RunObjectsTestAsync(StringBuilder log)
    {
        string folder = System.IO.Path.GetDirectoryName(SelfTest.LogPath)!;

        // A picture: export two strokes to PNG, then insert that file.
        InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
        pen.Size = new Size(8, 8);
        pen.Color = Microsoft.UI.Colors.DarkGreen;
        int s1 = _document.AddNew(SelfTest.MakeStroke(pen, new Point(40, 40), new Point(300, 120)));
        int s2 = _document.AddNew(SelfTest.MakeStroke(pen, new Point(40, 120), new Point(300, 40)));
        string png = System.IO.Path.Combine(folder, "objects-source.png");
        await ExportPngAsync(png);
        _document.Remove([s1, s2]);
        PageObject picture = await InsertImageFileAsync(png, new Point(60, 60));
        log.AppendLine($"picture {picture.Width}x{picture.Height} at {picture.X},{picture.Y}; asset exists " +
            System.IO.File.Exists(System.IO.Path.Combine(_assetFolder, picture.Asset)));

        // A PDF: a minimal two-page document written by hand.
        string pdf = System.IO.Path.Combine(folder, "objects-source.pdf");
        System.IO.File.WriteAllText(pdf, SelfTest.MinimalPdf(["First PDF page", "Second PDF page"]), Encoding.ASCII);
        int pages = await InsertPdfAsync(pdf, new Point(520, 60));
        log.AppendLine($"pdf pages inserted {pages}; objects now {_objects.Count}; " +
            string.Join(" | ", _objects.Skip(1).Select(o => $"{o.Width:0}x{o.Height:0}@{o.X:0},{o.Y:0}")));

        // A text box, typed into programmatically, then focused like a tap would.
        Toolbar.ActiveTool = TextButton;
        OnObjectToolPressed(new Point(80, 330));
        OnObjectToolReleased();
        await Task.Delay(400);
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot);
        log.AppendLine($"after tap on empty page: focused element {focused?.GetType().Name}, presenter mode {Presenter.InputProcessingConfiguration.Mode}");
        if (focused is Microsoft.UI.Xaml.Controls.TextBox box)
        {
            box.Text = "Typed text box\rsecond line";
        }

        await Task.Delay(300);
        PageObject text = _objects[^1];
        log.AppendLine($"text object \"{text.Text.Replace("\r", "\\r")}\" size {text.Width:0}x{text.Height:0}");

        // Ink over the picture and the PDF.
        pen.Color = Microsoft.UI.Colors.Red;
        _undo.Push(new AddStrokesStep([_document.AddNew(SelfTest.MakeStroke(pen, new Point(80, 100), new Point(1000, 260)))]));

        // Move and resize the picture as the tool would, then undo/redo everything.
        SelectObject(picture);
        // Grab inside the picture, away from its resize handles.
        OnObjectToolPressed(new Point(picture.X + 40, picture.Y + 40));
        OnObjectToolMoved(new Point(picture.X + 80, picture.Y + 60));
        OnObjectToolReleased();
        log.AppendLine($"picture moved to {picture.X},{picture.Y} (expect 100,80)");
        Root.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        await Task.Delay(300);

        int total = _objects.Count;
        int undone = 0;
        while (_undo.CanUndo)
        {
            Undo();
            undone++;
        }

        log.AppendLine($"undo x{undone}: objects {total} -> {_objects.Count}, strokes {_document.Container.GetStrokes().Count}");
        while (_undo.CanRedo)
        {
            Redo();
        }

        log.AppendLine($"redo: objects {_objects.Count}, strokes {_document.Container.GetStrokes().Count}; page {PageSurface.Width}x{PageSurface.Height}");
        SelectObject(picture);
        await Task.Delay(800);
    }

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
