using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace InkBoard;

/// <summary>
/// OneNote-style colour sets for the pen and highlighter flyouts. The reserved private-notes colour
/// (<see cref="PrivateInk"/>) must never appear here, nor any colour close enough to be removed by the
/// OBS colour key.
/// </summary>
public static class InkPalettes
{
    public static readonly IReadOnlyList<Color> Pens =
    [
        Rgb(0x000000), Rgb(0x404040), Rgb(0x7F7F7F), Rgb(0xA6A6A6),   // black, greys
        Rgb(0xC00000), Rgb(0xFF0000), Rgb(0xED7D31), Rgb(0xFFC000),   // dark red, red, orange, gold
        Rgb(0xBF9000), Rgb(0x7F6000), Rgb(0x843C0C), Rgb(0x92D050),   // ochre, brown, rust, light green
        Rgb(0x00B050), Rgb(0x00632B), Rgb(0x008080), Rgb(0x00B0F0),   // green, dark green, teal, light blue
        Rgb(0x0070C0), Rgb(0x002060), Rgb(0x7030A0), Rgb(0xB381D9),   // blue, navy, purple, lavender
        Rgb(0xE6007E), Rgb(0xFF66CC),                                 // pink, light pink
    ];

    public static readonly IReadOnlyList<Color> Highlighters =
    [
        Rgb(0xFFFF00), Rgb(0x99FF33), Rgb(0x33E0E0), Rgb(0xFF80C0),   // yellow, green, turquoise, pink
        Rgb(0xFFB000), Rgb(0xC8A2FF), Rgb(0xFF6060), Rgb(0x66B2FF),   // orange, lavender, red, blue
        Rgb(0xC0C0C0),                                                // grey
    ];

    public static IList<Brush> ToBrushes(IReadOnlyList<Color> colors) =>
        colors.Select(c => (Brush)new SolidColorBrush(c)).ToList();

    private static Color Rgb(uint rgb) =>
        Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
