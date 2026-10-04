using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace InkBoard;

// Page objects: typed text boxes and pictures (pasted images, rendered PDF pages) in a layer under the ink.
//
// The InkCanvas sits on top and owns pointer input, so the Text tool works like the lasso: the presenter
// is put in Mode = None and taps/drags arrive through InkUnprocessedInput. Objects are hit-tested here;
// a text box is edited by giving it keyboard focus.
public sealed partial class MainWindow
{
    private const double ResizeHandleSize = 18;
    private const double PdfRenderScale = 2;
    private const double PdfPageGap = 16;
    private const double MaxPastedImageWidth = 1200;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    private readonly List<PageObject> _objects = [];
    private readonly Dictionary<PageObject, FrameworkElement> _objectViews = [];

    private enum ObjectDrag { None, Move, Resize }

    private PageObject? _selectedObject;
    private ObjectDrag _objectDrag;
    private Point _objectDragStart;
    private Rect _objectStartRect;
    private bool _objectDragged;
    private string _textAtFocus = "";
    private PageObject? _newTextObject;

    /// <summary>Where this page's image files live. Phase 10 points it at the page's folder.</summary>
    private string _assetFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InkBoard", "unsaved-assets");

    /// <summary>Colour for new text boxes (RRGGBB).</summary>
    private string _textColor = "000000";

    private bool ObjectToolActive => ReferenceEquals(Toolbar.ActiveTool, TextButton);

    private void OnPrivateTextChanged(object sender, RoutedEventArgs e) =>
        _textColor = PrivateTextToggle.IsChecked == true ? PrivateInk.Hex : "000000";

    // ---- Views ----------------------------------------------------------------------------------

    private static Windows.UI.Color ParseColor(string rrggbb)
    {
        uint value = Convert.ToUInt32(rrggbb, 16);
        return Windows.UI.Color.FromArgb(0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private FrameworkElement CreateView(PageObject obj)
    {
        if (obj.Kind == PageObjectKind.Image)
        {
            return new Image
            {
                Source = new BitmapImage(new Uri(Path.Combine(_assetFolder, obj.Asset))),
                Stretch = Stretch.Fill,
            };
        }

        var brush = new SolidColorBrush(ParseColor(obj.Color));
        var transparent = new SolidColorBrush(Colors.Transparent);
        var box = new TextBox
        {
            // AcceptsReturn must be set before Text, or a multi-line text is cut at the first line break.
            AcceptsReturn = true,
            Text = obj.Text,
            FontSize = obj.FontSize,
            MinWidth = 24,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2),
            Foreground = brush,
            Background = transparent,
            IsSpellCheckEnabled = false,
        };
        // The app chrome is dark; a text box on the white page must not pick up dark-theme state colours.
        foreach (string state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            box.Resources["TextControlBackground" + state] = transparent;
            box.Resources["TextControlForeground" + state] = brush;
        }

        box.TextChanged += (_, _) => OnTextBoxEdited(obj, box);
        box.GotFocus += (_, _) => _textAtFocus = obj.Text;
        box.LostFocus += (_, _) => CommitText(obj);
        box.SizeChanged += (_, args) =>
        {
            obj.Width = args.NewSize.Width;
            obj.Height = args.NewSize.Height;
            UpdateObjectChrome();
            OnPageContentChanged();
        };
        return box;
    }

    private void PlaceView(PageObject obj)
    {
        FrameworkElement view = _objectViews[obj];
        Canvas.SetLeft(view, obj.X);
        Canvas.SetTop(view, obj.Y);
        if (obj.Kind == PageObjectKind.Image)
        {
            view.Width = obj.Width;
            view.Height = obj.Height;
        }
    }

    private void ShowObject(PageObject obj)
    {
        if (_objectViews.ContainsKey(obj))
        {
            return;
        }

        _objects.Add(obj);
        _objectViews[obj] = CreateView(obj);
        ObjectLayer.Children.Add(_objectViews[obj]);
        PlaceView(obj);
        LogObject(RecordedEventKind.ObjectAdd, obj);
        OnPageContentChanged();
    }

    private void HideObject(PageObject obj)
    {
        if (!_objectViews.Remove(obj, out FrameworkElement? view))
        {
            return;
        }

        _objects.Remove(obj);
        ObjectLayer.Children.Remove(view);
        LogObject(RecordedEventKind.ObjectRemove, obj);
        if (ReferenceEquals(_selectedObject, obj))
        {
            SelectObject(null);
        }

        OnPageContentChanged();
    }

    private void ClearObjects()
    {
        foreach (PageObject obj in _objects.ToList())
        {
            HideObject(obj);
        }
    }

    private void SetObjectRect(PageObject obj, Rect rect)
    {
        (obj.X, obj.Y) = (rect.X, rect.Y);
        if (obj.Kind == PageObjectKind.Image)
        {
            (obj.Width, obj.Height) = (rect.Width, rect.Height);
        }

        if (_objectViews.ContainsKey(obj))
        {
            PlaceView(obj);
            UpdateObjectChrome();
            LogObject(RecordedEventKind.ObjectRect, obj);
            OnPageContentChanged();
        }
    }

    private static Rect RectOf(PageObject obj) => new(obj.X, obj.Y, Math.Max(obj.Width, 1), Math.Max(obj.Height, 1));

    private Rect ObjectBounds() => _objects.Count == 0
        ? default
        : new Rect(0, 0, _objects.Max(o => o.Right), _objects.Max(o => o.Bottom));

    // ---- Add / remove with undo -----------------------------------------------------------------

    private void AddObjectWithUndo(PageObject obj)
    {
        ShowObject(obj);
        _undo.Push(new ActionStep(() => HideObject(obj), () => ShowObject(obj)));
    }

    private void RemoveObjectWithUndo(PageObject obj)
    {
        HideObject(obj);
        _undo.Push(new ActionStep(() => ShowObject(obj), () => HideObject(obj)));
    }

    // ---- Text editing ---------------------------------------------------------------------------

    private void OnTextBoxEdited(PageObject obj, TextBox box)
    {
        if (obj.Text != box.Text)
        {
            obj.Text = box.Text;
            MarkPageDirty();
            OnObjectTextTyped(obj);
        }
    }

    /// <summary>Hook for the recorder (typing is captured as it happens, not only when committed).</summary>
    partial void OnObjectTextTyped(PageObject obj);

    private void SetText(PageObject obj, string text)
    {
        bool changed = obj.Text != text;
        obj.Text = text;
        if (changed)
        {
            LogObject(RecordedEventKind.ObjectText, obj);
        }

        if (_objectViews.TryGetValue(obj, out FrameworkElement? view) && view is TextBox box && box.Text != text)
        {
            box.Text = text;
        }
    }

    private void CommitText(PageObject obj)
    {
        if (!_objectViews.ContainsKey(obj))
        {
            return;
        }

        bool isNew = ReferenceEquals(_newTextObject, obj);
        if (isNew)
        {
            _newTextObject = null;
        }

        if (string.IsNullOrWhiteSpace(obj.Text))
        {
            // An empty box left behind by a stray tap, or a box whose text was deleted.
            if (isNew)
            {
                HideObject(obj);
            }
            else
            {
                SetText(obj, _textAtFocus);
                RemoveObjectWithUndo(obj);
            }

            return;
        }

        if (isNew)
        {
            _undo.Push(new ActionStep(() => HideObject(obj), () => ShowObject(obj)));
            _textAtFocus = obj.Text;
            return;
        }

        if (obj.Text != _textAtFocus)
        {
            string before = _textAtFocus, after = obj.Text;
            _undo.Push(new ActionStep(() => SetText(obj, before), () => SetText(obj, after)));
            _textAtFocus = after;
        }
    }

    private void FocusText(PageObject obj)
    {
        if (_objectViews.TryGetValue(obj, out FrameworkElement? view) && view is TextBox box)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                box.Focus(FocusState.Programmatic);
                box.SelectionStart = box.Text.Length;
            });
        }
    }

    // ---- Selection chrome -----------------------------------------------------------------------

    private void SelectObject(PageObject? obj)
    {
        _selectedObject = obj;
        UpdateObjectChrome();
    }

    private void UpdateObjectChrome()
    {
        if (_selectedObject is not PageObject obj)
        {
            ObjectSelectionRect.Visibility = Visibility.Collapsed;
            ResizeHandle.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(ObjectSelectionRect, obj.X - 3);
        Canvas.SetTop(ObjectSelectionRect, obj.Y - 3);
        ObjectSelectionRect.Width = Math.Max(obj.Width, 1) + 6;
        ObjectSelectionRect.Height = Math.Max(obj.Height, 1) + 6;
        ObjectSelectionRect.Visibility = Visibility.Visible;

        bool resizable = obj.Kind == PageObjectKind.Image;
        Canvas.SetLeft(ResizeHandle, obj.Right - ResizeHandleSize / 2);
        Canvas.SetTop(ResizeHandle, obj.Bottom - ResizeHandleSize / 2);
        ResizeHandle.Visibility = resizable ? Visibility.Visible : Visibility.Collapsed;
    }

    private PageObject? HitTestObject(Point p) =>
        _objects.AsEnumerable().Reverse().FirstOrDefault(o => o.Contains(p.X, p.Y));

    private bool OnResizeHandle(Point p) =>
        _selectedObject is { Kind: PageObjectKind.Image } obj
        && Math.Abs(p.X - obj.Right) <= ResizeHandleSize && Math.Abs(p.Y - obj.Bottom) <= ResizeHandleSize;

    // ---- Tool input (from InkUnprocessedInput) --------------------------------------------------

    private void OnObjectToolPressed(Point p)
    {
        _objectDragged = false;
        _objectDragStart = p;

        if (OnResizeHandle(p))
        {
            _objectDrag = ObjectDrag.Resize;
            _objectStartRect = RectOf(_selectedObject!);
            return;
        }

        PageObject? hit = HitTestObject(p);
        if (hit is null)
        {
            // Tap on empty page: start typing there.
            Root.Focus(FocusState.Programmatic);
            var text = new PageObject
            {
                Kind = PageObjectKind.Text, X = p.X, Y = p.Y - 16, FontSize = 20, Color = _textColor,
            };
            // Not on the undo stack until something has been typed (see CommitText).
            ShowObject(text);
            _newTextObject = text;
            SelectObject(text);
            FocusText(text);
            return;
        }

        SelectObject(hit);
        _objectDrag = ObjectDrag.Move;
        _objectStartRect = RectOf(hit);
    }

    private void OnObjectToolMoved(Point p)
    {
        if (_objectDrag == ObjectDrag.None || _selectedObject is not PageObject obj)
        {
            return;
        }

        double dx = p.X - _objectDragStart.X, dy = p.Y - _objectDragStart.Y;
        if (!_objectDragged && Math.Abs(dx) < 3 && Math.Abs(dy) < 3)
        {
            return;
        }

        _objectDragged = true;
        Rect start = _objectStartRect;
        if (_objectDrag == ObjectDrag.Move)
        {
            SetObjectRect(obj, new Rect(Math.Max(0, start.X + dx), Math.Max(0, start.Y + dy), start.Width, start.Height));
        }
        else
        {
            double width = Math.Max(24, start.Width + dx);
            SetObjectRect(obj, new Rect(start.X, start.Y, width, width * start.Height / start.Width));
        }
    }

    private void OnObjectToolReleased()
    {
        ObjectDrag drag = _objectDrag;
        _objectDrag = ObjectDrag.None;
        if (_selectedObject is not PageObject obj || drag == ObjectDrag.None)
        {
            return;
        }

        if (_objectDragged)
        {
            Rect before = _objectStartRect, after = RectOf(obj);
            _undo.Push(new ActionStep(() => SetObjectRect(obj, before), () => SetObjectRect(obj, after)));
        }
        else if (obj.Kind == PageObjectKind.Text)
        {
            FocusText(obj);
        }
    }

    private bool DeleteSelectedObject()
    {
        if (_selectedObject is not PageObject obj)
        {
            return false;
        }

        RemoveObjectWithUndo(obj);
        return true;
    }

    // ---- Images and PDFs ------------------------------------------------------------------------

    /// <summary>Top-left of the visible part of the page, slightly inset, in page coordinates.</summary>
    private Point ViewportOrigin()
    {
        float zoom = CanvasScroller.ZoomFactor;
        return new Point(CanvasScroller.HorizontalOffset / zoom + 40, CanvasScroller.VerticalOffset / zoom + 40);
    }

    private string NewAssetPath(out string name)
    {
        Directory.CreateDirectory(_assetFolder);
        name = Guid.NewGuid().ToString("N") + ".png";
        return Path.Combine(_assetFolder, name);
    }

    private PageObject AddImageObject(string assetName, Point at, double width, double height)
    {
        if (width > MaxPastedImageWidth)
        {
            (width, height) = (MaxPastedImageWidth, height * MaxPastedImageWidth / width);
        }

        var image = new PageObject
        {
            Kind = PageObjectKind.Image, Asset = assetName, X = at.X, Y = at.Y, Width = width, Height = height,
        };
        AddObjectWithUndo(image);
        return image;
    }

    /// <summary>Re-encodes any decodable image stream as a PNG asset. Returns its size in DIPs.</summary>
    private async Task<(string Name, double Width, double Height)> SaveImageAssetAsync(IRandomAccessStream source)
    {
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(source);
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        string path = NewAssetPath(out string name);
        using (IRandomAccessStream target = await FileRandomAccessStream.OpenAsync(
                   path, FileAccessMode.ReadWrite, StorageOpenOptions.None, FileOpenDisposition.CreateAlways))
        {
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, target);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
        }

        // Size in DIPs: honour the image's own DPI (an InkBoard PNG export is 192 DPI).
        double scale = decoder.DpiX > 0 ? 96 / decoder.DpiX : 1;
        return (name, decoder.PixelWidth * scale, decoder.PixelHeight * scale);
    }

    internal async Task<PageObject> InsertImageFileAsync(string path, Point at)
    {
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
        (string name, double width, double height) = await SaveImageAssetAsync(stream);
        return AddImageObject(name, at, width, height);
    }

    /// <summary>
    /// Renders every page of a PDF with the OS PDF renderer and stacks the pages down the page as pictures,
    /// like a OneNote printout. Returns the number of pages.
    /// </summary>
    internal async Task<int> InsertPdfAsync(string path, Point at)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        PdfDocument document = await PdfDocument.LoadFromFileAsync(file);
        double y = at.Y;
        for (uint i = 0; i < document.PageCount; i++)
        {
            using PdfPage page = document.GetPage(i);
            Size size = page.Size;
            string assetPath = NewAssetPath(out string name);
            using (IRandomAccessStream target = await FileRandomAccessStream.OpenAsync(
                       assetPath, FileAccessMode.ReadWrite, StorageOpenOptions.None, FileOpenDisposition.CreateAlways))
            {
                await page.RenderToStreamAsync(target, new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)(size.Width * PdfRenderScale),
                    DestinationHeight = (uint)(size.Height * PdfRenderScale),
                    BackgroundColor = Colors.White,
                });
            }

            PageObject image = AddImageObject(name, new Point(at.X, y), size.Width, size.Height);
            y = image.Bottom + PdfPageGap;
        }

        return (int)document.PageCount;
    }

    private async Task<string> InsertFilesAsync(IEnumerable<string> paths, Point at)
    {
        int images = 0, pdfPages = 0;
        foreach (string path in paths)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".pdf")
            {
                pdfPages += await InsertPdfAsync(path, at);
            }
            else if (ImageExtensions.Contains(extension))
            {
                await InsertImageFileAsync(path, at);
                images++;
            }
            else
            {
                continue;
            }

            at = new Point(at.X, ObjectBounds().Bottom + PdfPageGap);
        }

        return images + pdfPages == 0
            ? "Nothing to insert (images and PDFs only)"
            : $"Inserted {images} image(s), {pdfPages} PDF page(s)";
    }

    /// <summary>Pastes a bitmap or copied image/PDF files from the clipboard. False if there was none.</summary>
    private async Task<bool> PasteObjectsAsync()
    {
        DataPackageView content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await content.GetStorageItemsAsync();
            StatusText.Text = await InsertFilesAsync(items.Select(i => i.Path), ViewportOrigin());
            return true;
        }

        if (content.Contains(StandardDataFormats.Bitmap))
        {
            RandomAccessStreamReference reference = await content.GetBitmapAsync();
            using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
            (string name, double width, double height) = await SaveImageAssetAsync(stream);
            AddImageObject(name, ViewportOrigin(), width, height);
            StatusText.Text = "Pasted image";
            return true;
        }

        return false;
    }
}
