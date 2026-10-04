using System;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

/// <summary>
/// Calligraphy nib: a flat rectangular pen tip held at 45 degrees. The stroke shape comes entirely from
/// the platform renderer applying <see cref="InkDrawingAttributes.PenTipTransform"/>.
/// </summary>
public sealed partial class CalligraphyPen : InkToolbarCustomPen
{
    private const float NibAngleRadians = MathF.PI / 4;

    protected override InkDrawingAttributes CreateInkDrawingAttributesCore(Brush brush, double strokeWidth)
    {
        return new InkDrawingAttributes
        {
            PenTip = PenTipShape.Rectangle,
            Size = new Size(strokeWidth, Math.Max(1, strokeWidth / 5)),
            Color = (brush as SolidColorBrush)?.Color ?? Colors.Black,
            FitToCurve = true,
            IgnorePressure = false,
            PenTipTransform = Matrix3x2.CreateRotation(NibAngleRadians),
        };
    }
}
