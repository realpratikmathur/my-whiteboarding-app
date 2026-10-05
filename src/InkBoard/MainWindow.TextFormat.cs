using System;
using System.Globalization;
using System.Linq;
using InkBoard.Core;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkBoard;

// Font, size, bold and italic for text boxes (the format bar shown with the Text tool).
//
// The style belongs to a whole text box, not to individual words: text boxes are plain TextBox controls.
// What is chosen on the bar applies to the selected text box and becomes the default for new ones.
public sealed partial class MainWindow
{
    // The sizes OneNote offers.
    private static readonly double[] FontSizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    /// <summary>Style for new text boxes; only its style properties are used.</summary>
    private readonly PageObject _textStyle = new() { Kind = PageObjectKind.Text };

    private bool _updatingFormatBar;

    private void ConfigureTextFormat()
    {
        _updatingFormatBar = true;
        FontBox.ItemsSource = CanvasTextFormat.GetSystemFontFamilies().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        FontSizeBox.ItemsSource = FontSizes.Select(FormatSize).ToList();
        _updatingFormatBar = false;
        ShowStyleInFormatBar(_textStyle);
    }

    private static string FormatSize(double size) => size.ToString("0.#", CultureInfo.CurrentCulture);

    private static void ApplyStyleToView(PageObject obj, TextBox box)
    {
        box.FontFamily = new FontFamily(obj.FontFamily);
        box.FontSize = obj.FontSize;
        box.FontWeight = obj.Bold ? FontWeights.Bold : FontWeights.Normal;
        box.FontStyle = obj.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
    }

    /// <summary>Gives a text box the font, size, bold and italic of <paramref name="style"/>.</summary>
    internal void SetTextStyle(PageObject obj, PageObject style)
    {
        if (obj.HasSameStyleAs(style))
        {
            return;
        }

        obj.CopyStyleFrom(style);
        if (_objectViews.TryGetValue(obj, out FrameworkElement? view) && view is TextBox box)
        {
            ApplyStyleToView(obj, box);
        }

        LogObject(RecordedEventKind.ObjectStyle, obj);
        MarkPageDirty();
        if (ReferenceEquals(obj, _selectedObject))
        {
            ShowStyleInFormatBar(obj);
        }
    }

    private void ShowStyleInFormatBar(PageObject style)
    {
        _updatingFormatBar = true;
        FontBox.SelectedItem = style.FontFamily;
        string size = FormatSize(style.FontSize);
        FontSizeBox.SelectedItem = (FontSizeBox.ItemsSource as System.Collections.Generic.List<string>)?.Contains(size) == true ? size : null;
        FontSizeBox.Text = size;
        BoldToggle.IsChecked = style.Bold;
        ItalicToggle.IsChecked = style.Italic;
        _updatingFormatBar = false;
    }

    private void OnTextFormatChanged(object sender, RoutedEventArgs e) => ApplyFormatBar(FontSizeBox.SelectedItem as string);

    private void OnFontSizeSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        ApplyFormatBar(args.Text);
        args.Handled = true;   // a typed size need not be one of the listed ones
    }

    /// <summary>Takes what the bar shows as the style for new text and for the selected text box.</summary>
    private void ApplyFormatBar(string? sizeText)
    {
        if (_updatingFormatBar)
        {
            return;
        }

        var style = new PageObject
        {
            FontFamily = FontBox.SelectedItem as string ?? _textStyle.FontFamily,
            FontSize = double.TryParse(sizeText, NumberStyles.Float, CultureInfo.CurrentCulture, out double size)
                ? Math.Clamp(size, 6, 400)
                : _textStyle.FontSize,
            Bold = BoldToggle.IsChecked == true,
            Italic = ItalicToggle.IsChecked == true,
        };
        _textStyle.CopyStyleFrom(style);

        if (_selectedObject is { Kind: PageObjectKind.Text } text && !_replaying && !text.HasSameStyleAs(style))
        {
            PageObject before = text.Clone();
            SetTextStyle(text, style);
            if (!ReferenceEquals(text, _newTextObject))
            {
                _undo.Push(new ActionStep(() => SetTextStyle(text, before), () => SetTextStyle(text, style)));
            }

            // Using the bar took the keyboard away from the text box; give it back so typing continues.
            FocusText(text);
        }
        else
        {
            ShowStyleInFormatBar(_textStyle);
        }
    }
}
