using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class PrivateInkLutTests
{
    // The private colour: pure magenta.
    private const byte R = 0xFF, G = 0x00, B = 0xFF;

    private static (byte, byte, byte) Map(int r, int g, int b) => PrivateInkLut.Map(r, g, b, R, G, B);

    [Fact]
    public void The_private_colour_becomes_white()
    {
        Assert.Equal(((byte)255, (byte)255, (byte)255), Map(255, 0, 255));
    }

    [Theory]
    [InlineData(255, 64, 255)]
    [InlineData(255, 128, 255)]
    [InlineData(255, 200, 255)]
    [InlineData(255, 245, 255)]
    public void Every_blend_of_the_private_colour_with_white_becomes_white(int r, int g, int b)
    {
        // These are the anti-aliased edge pixels of private ink on the white page.
        Assert.Equal(((byte)255, (byte)255, (byte)255), Map(r, g, b));
    }

    [Theory]
    [InlineData(0x00, 0x00, 0x00)]   // black
    [InlineData(0xFF, 0x00, 0x00)]   // red
    [InlineData(0x00, 0x70, 0xC0)]   // blue
    [InlineData(0x70, 0x30, 0xA0)]   // purple
    [InlineData(0xB3, 0x81, 0xD9)]   // lavender pen
    [InlineData(0xE6, 0x00, 0x7E)]   // pink pen
    [InlineData(0xFF, 0x66, 0xCC)]   // light pink pen
    [InlineData(0xFF, 0x80, 0xC0)]   // pink highlighter
    [InlineData(0xC8, 0xA2, 0xFF)]   // lavender highlighter
    [InlineData(0xFF, 0xFF, 0x00)]   // yellow highlighter
    [InlineData(0xC0, 0xC0, 0xC0)]   // grey highlighter
    public void Regular_ink_colours_are_left_exactly_as_they_are(int r, int g, int b)
    {
        Assert.Equal(((byte)r, (byte)g, (byte)b), Map(r, g, b));
        Assert.True(PrivateInkLut.DistanceToInkLine(r, g, b, R, G, B) > PrivateInkLut.Radius + PrivateInkLut.Feather + 20);
    }

    [Fact]
    public void White_stays_white()
    {
        Assert.Equal(((byte)255, (byte)255, (byte)255), Map(255, 255, 255));
    }

    [Fact]
    public void Image_uses_the_obs_lut_layout()
    {
        byte[] image = PrivateInkLut.BuildImage(R, G, B);
        (byte R, byte G, byte B) At(int x, int y) => (image[(y * 512 + x) * 4 + 2], image[(y * 512 + x) * 4 + 1], image[(y * 512 + x) * 4]);

        Assert.Equal(512 * 512 * 4, image.Length);
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(0, 0));         // black
        Assert.Equal(((byte)255, (byte)0, (byte)0), At(63, 0));      // x is red
        Assert.Equal(((byte)0, (byte)255, (byte)0), At(0, 63));      // y is green
        Assert.Equal(((byte)0, (byte)0, (byte)4), At(64, 0));        // next tile is the next blue level
        Assert.Equal(((byte)255, (byte)255, (byte)255), At(511, 511));

        // The magenta corner of the cube (r=63, g=0, b=63) is in the last tile and maps to white.
        Assert.Equal(((byte)255, (byte)255, (byte)255), At(7 * 64 + 63, 7 * 64));
    }
}
