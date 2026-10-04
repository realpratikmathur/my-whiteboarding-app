namespace InkBoard.Core;

public enum PageObjectKind
{
    Text,
    Image,
}

/// <summary>Something on the page that is not ink: a typed text box or a picture (pasted image, PDF page).</summary>
public sealed class PageObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PageObjectKind Kind { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    // Text
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 20;

    /// <summary>Text colour as RRGGBB hex.</summary>
    public string Color { get; set; } = "000000";

    // Image
    /// <summary>File name inside the page's assets folder.</summary>
    public string Asset { get; set; } = "";

    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}
