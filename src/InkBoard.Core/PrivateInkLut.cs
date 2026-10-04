namespace InkBoard.Core;

/// <summary>
/// Builds the colour lookup table for OBS's "Apply LUT" filter that hides private notes.
///
/// Anti-aliased ink is not one colour: its edges are blends between the ink colour and the white page.
/// The table therefore turns every colour within <see cref="Radius"/> of the straight line from white to
/// the private colour into white, and leaves all other colours exactly as they are. A single colour-key
/// sphere cannot do that without either leaving an outline or eating the edges of other inks.
///
/// Format: 64x64x64 table as a 512x512 image of 8x8 tiles; inside a tile x is red and y is green, the tile
/// index is blue (the layout of OBS's own LUT files).
/// </summary>
public static class PrivateInkLut
{
    public const int GridSize = 64;
    public const int ImageSize = 512;

    /// <summary>How far (in 0-255 colour levels) from the white-to-ink line a colour is still removed.</summary>
    public const double Radius = 12;

    /// <summary>Width of the soft border of the removed region, in colour levels.</summary>
    public const double Feather = 4;

    /// <summary>Distance from a colour to the line segment between white and the ink colour.</summary>
    public static double DistanceToInkLine(double r, double g, double b, byte inkR, byte inkG, byte inkB)
    {
        double dx = inkR - 255.0, dy = inkG - 255.0, dz = inkB - 255.0;
        double lengthSquared = dx * dx + dy * dy + dz * dz;
        double t = lengthSquared == 0 ? 0 : Math.Clamp(((r - 255) * dx + (g - 255) * dy + (b - 255) * dz) / lengthSquared, 0, 1);
        double cx = 255 + t * dx, cy = 255 + t * dy, cz = 255 + t * dz;
        return Math.Sqrt((r - cx) * (r - cx) + (g - cy) * (g - cy) + (b - cz) * (b - cz));
    }

    /// <summary>What the table stores for one colour: white inside the removed region, itself outside.</summary>
    public static (byte R, byte G, byte B) Map(double r, double g, double b, byte inkR, byte inkG, byte inkB)
    {
        double keep = Math.Clamp((DistanceToInkLine(r, g, b, inkR, inkG, inkB) - Radius) / Feather, 0, 1);
        return (Blend(r, keep), Blend(g, keep), Blend(b, keep));

        static byte Blend(double value, double keep) => (byte)Math.Round(value * keep + 255 * (1 - keep));
    }

    /// <summary>The LUT image as 512x512 BGRA pixels.</summary>
    public static byte[] BuildImage(byte inkR, byte inkG, byte inkB)
    {
        var pixels = new byte[ImageSize * ImageSize * 4];
        for (int blue = 0; blue < GridSize; blue++)
        {
            for (int green = 0; green < GridSize; green++)
            {
                for (int red = 0; red < GridSize; red++)
                {
                    (byte r, byte g, byte b) = Map(Level(red), Level(green), Level(blue), inkR, inkG, inkB);
                    int x = blue % 8 * GridSize + red, y = blue / 8 * GridSize + green;
                    int i = (y * ImageSize + x) * 4;
                    (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
                }
            }
        }

        return pixels;

        static double Level(int index) => index * 255.0 / (GridSize - 1);
    }
}
