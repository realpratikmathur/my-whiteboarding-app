using System;
using System.Threading.Tasks;
using InkBoard.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace InkBoard;

/// <summary>
/// The one reserved colour for "notes to myself": pure magenta. Ink and text in this colour are removed
/// from the video by an OBS "Apply LUT" filter (see <see cref="PrivateInkLut"/>), are never recorded, and
/// stay on the page during replay.
///
/// Chosen by measurement on real OBS recordings of a test card (self-test scenario "obs-chart"): with the
/// LUT, magenta ink and text left no pixel behind, and of the candidates tried (magenta, cyan and green
/// families) it disturbed the regular pen, pencil and highlighter colours least. It is also the easiest of
/// them to read on a white page. No palette in <see cref="InkPalettes"/> may contain it.
/// </summary>
public static class PrivateInk
{
    public static readonly Color Color = Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF);

    public static string Hex => $"{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    public static bool IsPrivate(Color color) => color.R == Color.R && color.G == Color.G && color.B == Color.B;

    public static bool IsPrivate(InkStroke stroke) => IsPrivate(stroke.DrawingAttributes.Color);

    public static bool IsPrivate(PageObject obj) =>
        obj.Kind == PageObjectKind.Text && string.Equals(obj.Color, Hex, StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes the LUT image to use with OBS's "Apply LUT" filter.</summary>
    public static async Task SaveObsLutAsync(string path)
    {
        byte[] pixels = PrivateInkLut.BuildImage(Color.R, Color.G, Color.B);
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(
            path, FileAccessMode.ReadWrite, StorageOpenOptions.None, FileOpenDisposition.CreateAlways);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, PrivateInkLut.ImageSize, PrivateInkLut.ImageSize, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}
