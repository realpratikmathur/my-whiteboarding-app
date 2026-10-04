using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using InkPersistenceFormat = Windows.UI.Input.Inking.InkPersistenceFormat;
using InkStroke = Windows.UI.Input.Inking.InkStroke;
using OsInkStrokeContainer = Windows.UI.Input.Inking.InkStrokeContainer;

namespace InkBoard;

// Persistence and interop: .isf save/load, clipboard, PNG export.
public sealed partial class MainWindow
{
    private const float ExportDpi = 192;
    private const float ExportMargin = 20;

    // ---- ISF ------------------------------------------------------------------------------------

    internal async Task SaveIsfAsync(string path)
    {
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(
            path, FileAccessMode.ReadWrite, StorageOpenOptions.None, FileOpenDisposition.CreateAlways);
        using IOutputStream output = stream.GetOutputStreamAt(0);
        await _document.Container.SaveAsync(output, InkPersistenceFormat.Isf);
        await output.FlushAsync();
    }

    internal async Task LoadIsfAsync(string path)
    {
        // Parse into a scratch container first so a bad file cannot wipe the canvas.
        var loaded = new OsInkStrokeContainer();
        using (IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read))
        using (IInputStream input = stream.GetInputStreamAt(0))
        {
            await loaded.LoadAsync(input);
        }

        ClearSelection();
        IReadOnlyList<int> removed = _document.ClearAll();
        List<int> added = loaded.GetStrokes().Select(stroke => _document.AddNew(stroke.Clone())).ToList();
        _undo.Push(new ReplaceStrokesStep(removed, added));
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = "Whiteboard",
            DefaultFileExtension = ".isf",
        };
        picker.FileTypeChoices.Add("Ink Serialized Format", [".isf"]);
        await RunFileCommandAsync(await picker.PickSaveFileAsync(), SaveIsfAsync, "Saved");
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(AppWindow.Id);
        picker.FileTypeFilter.Add(".isf");
        picker.FileTypeFilter.Add(".gif");
        await RunFileCommandAsync(await picker.PickSingleFileAsync(), LoadIsfAsync, "Opened");
    }

    private async void OnExportPngClick(object sender, RoutedEventArgs e)
    {
        if (_document.Container.GetStrokes().Count == 0)
        {
            StatusText.Text = "Nothing to export";
            return;
        }

        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = "Whiteboard",
            DefaultFileExtension = ".png",
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        await RunFileCommandAsync(await picker.PickSaveFileAsync(), ExportPngAsync, "Exported");
    }

    private async Task RunFileCommandAsync(PickFileResult? picked, Func<string, Task> command, string verb)
    {
        if (picked is null)
        {
            return;
        }

        try
        {
            await command(picked.Path);
            StatusText.Text = $"{verb} {Path.GetFileName(picked.Path)}";
        }
        catch (Exception e)
        {
            StatusText.Text = $"{Path.GetFileName(picked.Path)}: {e.Message}";
        }
    }

    // ---- PNG ------------------------------------------------------------------------------------

    /// <summary>
    /// Rasterises the strokes with the platform ink renderer (IInkD2DRenderer) into a Win2D render target,
    /// on a white background cropped to the ink bounds.
    /// </summary>
    internal async Task ExportPngAsync(string path)
    {
        IReadOnlyList<InkStroke> strokes = _document.Container.GetStrokes();
        Rect bounds = _document.Container.BoundingRect;

        CanvasDevice device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(
            device,
            (float)bounds.Width + 2 * ExportMargin,
            (float)bounds.Height + 2 * ExportMargin,
            ExportDpi);
        using (CanvasDrawingSession session = target.CreateDrawingSession())
        {
            session.Clear(Colors.White);
            session.Transform = Matrix3x2.CreateTranslation(
                ExportMargin - (float)bounds.X, ExportMargin - (float)bounds.Y);
            PlatformInkRenderer.Draw(session, strokes);
        }

        await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
    }

    // ---- Clipboard ------------------------------------------------------------------------------

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopySelection();

    private void OnCutClick(object sender, RoutedEventArgs e)
    {
        if (CopySelection())
        {
            DeleteSelection();
        }
    }

    private bool CopySelection()
    {
        if (_document.SelectedHandles().Count == 0)
        {
            StatusText.Text = "Select ink with the lasso first";
            return false;
        }

        _document.Container.CopySelectedToClipboard();
        StatusText.Text = "Copied";
        return true;
    }

    private void OnPasteClick(object sender, RoutedEventArgs e)
    {
        if (!_document.Container.CanPasteFromClipboard())
        {
            StatusText.Text = "No ink on the clipboard";
            return;
        }

        ClearSelection();
        var existing = _document.Container.GetStrokes().Select(s => s.Id).ToHashSet();

        // Top-left of the visible part of the canvas, in canvas coordinates.
        float zoom = CanvasScroller.ZoomFactor;
        var position = new Point(CanvasScroller.HorizontalOffset / zoom + 40, CanvasScroller.VerticalOffset / zoom + 40);
        Rect bounds = _document.Container.PasteFromClipboard(position);

        IReadOnlyList<int> pasted = _document.Track(
            _document.Container.GetStrokes().Where(s => !existing.Contains(s.Id)));
        if (pasted.Count == 0)
        {
            return;
        }

        _undo.Push(new AddStrokesStep(pasted));

        // Leave the pasted ink selected with the lasso so it can be dragged into place.
        Toolbar.ActiveTool = LassoButton;
        _document.SelectOnly(pasted);
        ShowSelection(bounds);
        StatusText.Text = $"Pasted {pasted.Count} stroke(s)";
    }
}
