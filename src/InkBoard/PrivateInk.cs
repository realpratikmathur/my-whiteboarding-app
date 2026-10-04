using InkBoard.Core;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace InkBoard;

/// <summary>
/// The one reserved colour for "notes to myself". Ink and text in this colour are removed from the video
/// by an OBS Color Key filter, are never recorded, and stay on the page during replay.
/// </summary>
public static class PrivateInk
{
    /// <summary>Placeholder until Phase 12 picks the colour from real OBS recordings.</summary>
    public static readonly Color Color = Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF);

    public static string Hex => $"{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    public static bool IsPrivate(Color color) => color.R == Color.R && color.G == Color.G && color.B == Color.B;

    public static bool IsPrivate(InkStroke stroke) => IsPrivate(stroke.DrawingAttributes.Color);

    public static bool IsPrivate(PageObject obj) =>
        obj.Kind == PageObjectKind.Text && string.Equals(obj.Color, Hex, System.StringComparison.OrdinalIgnoreCase);
}
