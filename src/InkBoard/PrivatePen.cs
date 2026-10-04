using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

/// <summary>
/// The "notes to myself" pen: an ordinary round pen that always writes in <see cref="PrivateInk.Color"/>,
/// whatever the flyout says.
/// </summary>
public sealed partial class PrivatePen : InkToolbarCustomPen
{
    protected override InkDrawingAttributes CreateInkDrawingAttributesCore(Brush brush, double strokeWidth) => new()
    {
        PenTip = PenTipShape.Circle,
        Size = new Size(strokeWidth, strokeWidth),
        Color = PrivateInk.Color,
        FitToCurve = true,
        IgnorePressure = false,
    };
}
