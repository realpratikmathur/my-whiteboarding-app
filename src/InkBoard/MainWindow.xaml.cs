using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Input.Inking;

namespace InkBoard;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ConfigureInk();
    }

    private void ConfigureInk()
    {
        var presenter = InkSurface.InkPresenter;

        // Default is pen only.
        presenter.InputDeviceTypes =
            CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Touch | CoreInputDeviceTypes.Mouse;

        // Ballpoint: round tip, platform curve fitting, pressure and tilt left to the platform.
        InkDrawingAttributes attributes = presenter.CopyDefaultDrawingAttributes();
        attributes.PenTip = PenTipShape.Circle;
        attributes.Color = Colors.Black;
        attributes.Size = new Size(4, 4);
        attributes.FitToCurve = true;
        attributes.IgnorePressure = false;
        attributes.IgnoreTilt = false;
        presenter.UpdateDefaultDrawingAttributes(attributes);
    }
}
