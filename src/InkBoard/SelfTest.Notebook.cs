using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InkBoard.Core;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenarios for the notebook: "notebook-write" fills pages and saves; a second launch with
// "notebook-read" checks that everything comes back by itself.
public sealed partial class MainWindow
{
    private string TreeOutline() =>
        string.Join(" ", _notebook.AllPages().Select(p => new string('>', _notebook.DepthOf(p) - 1) + p.Title));

    internal async Task RunNotebookWriteTestAsync(StringBuilder log)
    {
        string folder = Path.GetDirectoryName(SelfTest.LogPath)!;
        log.AppendLine($"notebook folder {_notebookFolder}; first page \"{_currentPage?.Title}\"");

        InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
        pen.Size = new Size(6, 6);
        _undo.Push(new AddStrokesStep(
        [
            _document.AddNew(SelfTest.MakeStroke(pen, new Point(100, 100), new Point(600, 140))),
            _document.AddNew(SelfTest.MakeStroke(InkDrawingAttributes.CreateForPencil(), new Point(100, 200), new Point(600, 240))),
        ]));
        string png = Path.Combine(folder, "notebook-source.png");
        await ExportPngAsync(png);
        await InsertImageFileAsync(png, new Point(700, 100));
        AddObjectWithUndo(new PageObject { Kind = PageObjectKind.Text, Text = "Saved text\rline two", X = 100, Y = 320 });
        _undo.Push(new AddStrokesStep([_document.AddNew(SelfTest.MakeStroke(pen, new Point(100, 9000), new Point(600, 9050)))]));
        await Task.Delay(300);

        await AddPageAsync("Lesson 2");
        log.AppendLine($"new page open: \"{_currentPage?.Title}\", strokes {_document.Container.GetStrokes().Count}, " +
            $"objects {_objects.Count}, can undo {_undo.CanUndo} (expect 0, 0, False)");
        NotebookPage? part = await AddSubpageAsync("Part A");
        pen.Color = Microsoft.UI.Colors.Blue;
        _undo.Push(new AddStrokesStep([_document.AddNew(SelfTest.MakeStroke(pen, new Point(150, 150), new Point(500, 400)))]));
        await AddSubpageAsync("Detail");
        log.AppendLine("fourth level refused: " + (await AddSubpageAsync("Nope") is null));
        await OpenPageAsync(part!);
        await SaveCurrentPageAsync();
        log.AppendLine("tree: " + TreeOutline());
        log.AppendLine($"open at exit: \"{_currentPage?.Title}\" with {_document.Container.GetStrokes().Count} stroke(s)");
    }

    internal async Task RunNotebookReadTestAsync(StringBuilder log)
    {
        log.AppendLine("tree: " + TreeOutline());
        log.AppendLine($"opened by itself: \"{_currentPage?.Title}\" with {_document.Container.GetStrokes().Count} stroke(s) (expect Part A, 1)");

        await OpenPageAsync(_notebook.Pages[0]);
        log.AppendLine($"\"{_currentPage?.Title}\": strokes {_document.Container.GetStrokes().Count} (expect 3), kinds " +
            string.Join(",", _document.Container.GetStrokes().Select(s => s.DrawingAttributes.Kind)));
        log.AppendLine("  objects: " + string.Join(" | ", _objects.Select(o =>
            o.Kind == PageObjectKind.Text ? "Text:" + o.Text.Replace("\r", "\\r") : $"Image:{o.Width:0}x{o.Height:0}")));
        log.AppendLine($"  page size {PageSurface.Width}x{PageSurface.Height} (expect height >= 10000); picture files exist " +
            _objects.Where(o => o.Kind == PageObjectKind.Image).All(o => File.Exists(Path.Combine(_assetFolder, o.Asset))));
        await Task.Delay(800);

        string pages = Path.Combine(_notebookFolder, "pages");
        int before = Directory.GetDirectories(pages).Length;
        await DeletePageAsync(_notebook.Pages[1]);
        log.AppendLine($"after deleting Lesson 2: tree {TreeOutline()}; page folders {before} -> {Directory.GetDirectories(pages).Length}");
    }
}
