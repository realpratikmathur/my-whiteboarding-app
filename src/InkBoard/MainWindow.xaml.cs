using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI.Core;
// Presenter-level types are the WinUI 3 proxies (Microsoft.UI.Xaml.Controls); stroke-level types are the
// OS ones. Windows.UI.Input.Inking is not imported wholesale because several names exist in both.
using InkDrawingAttributes = Windows.UI.Input.Inking.InkDrawingAttributes;
using InkModelerAttributes = Windows.UI.Input.Inking.InkModelerAttributes;
using PenTipShape = Windows.UI.Input.Inking.PenTipShape;

namespace InkBoard;

public sealed partial class MainWindow : Window
{
    private readonly PenHaptics _haptics = new();

    // Platform defaults, captured before the user touches the debug panel.
    private TimeSpan _defaultPredictionTime;
    private float _defaultScalingFactor;
    private bool _defaultVelocityPressure;

    private bool _modelerOverridden;
    private bool _updatingModelerUi;
    private bool _barrelErasing;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureInk();
        ConfigureBarrelButtonErase();
        ConfigureHaptics();
        ConfigureModelerPanel();
    }

    private InkPresenter Presenter => InkSurface.InkPresenter;

    private void ConfigureInk()
    {
        // Default is pen only.
        Presenter.InputDeviceTypes =
            CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Touch | CoreInputDeviceTypes.Mouse;

        // Ballpoint: round tip, platform curve fitting, pressure and tilt left to the platform.
        // The InkToolbar takes over the attributes as soon as a tool is selected.
        InkDrawingAttributes attributes = Presenter.CopyDefaultDrawingAttributes();
        attributes.PenTip = PenTipShape.Circle;
        attributes.Color = Colors.Black;
        attributes.Size = new Size(4, 4);
        attributes.FitToCurve = true;
        attributes.IgnorePressure = false;
        attributes.IgnoreTilt = false;
        Presenter.UpdateDefaultDrawingAttributes(attributes);

        Toolbar.Loaded += (_, _) => Toolbar.ActiveTool ??= BallpointButton;
        Toolbar.ActiveToolChanged += (_, _) => OnToolbarChangedInk();
        Toolbar.InkDrawingAttributesChanged += (_, _) => OnToolbarChangedInk();
    }

    private void OnToolbarChangedInk()
    {
        // The toolbar pushes fresh attributes for the selected tool, which resets the modeler settings.
        ApplyModelerOverrides();
        ToolStatus.Text = $"Tool: {Toolbar.ActiveTool?.ToolKind}";
    }

    // ---- Barrel button erases -------------------------------------------------------------------

    private void ConfigureBarrelButtonErase()
    {
        Presenter.InputConfiguration.IsPrimaryBarrelButtonInputEnabled = true;
        Presenter.InputConfiguration.IsEraserInputEnabled = true;

        // Barrel-button drags are processed by the presenter (in whatever Mode is current) instead of
        // being handed to UnprocessedInput. The mode is flipped to Erasing while the button is held.
        Presenter.InputProcessingConfiguration.RightDragAction = InkInputRightDragAction.AllowProcessing;

        Presenter.UnprocessedInput.PointerHovered += (_, args) => OnPenHover(IsBarrelPressed(args));
        Presenter.UnprocessedInput.PointerExited += (_, _) => OnPenHover(barrelPressed: false);
    }

    private static bool IsBarrelPressed(PointerEventArgs args)
    {
        var point = args.CurrentPoint;
        return point.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen
            && point.Properties.IsBarrelButtonPressed;
    }

    private void OnPenHover(bool barrelPressed)
    {
        // Ink events are not documented to arrive on the UI thread; always hop.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (barrelPressed == _barrelErasing)
            {
                return;
            }

            _barrelErasing = barrelPressed;
            bool eraserToolActive = Toolbar.ActiveTool?.ToolKind == InkToolbarTool.Eraser;
            Presenter.InputProcessingConfiguration.Mode = barrelPressed || eraserToolActive
                ? InkInputProcessingMode.Erasing
                : InkInputProcessingMode.Inking;
            BarrelStatus.Text = barrelPressed ? "Barrel button: erasing" : "Barrel button: released";
        });
    }

    // ---- Haptics --------------------------------------------------------------------------------

    private void ConfigureHaptics()
    {
        Presenter.StrokeInput.StrokeStarted += (_, args) =>
        {
            uint pointerId = args.CurrentPoint.PointerId;
            DispatcherQueue.TryEnqueue(() =>
            {
                _haptics.Start(pointerId, Toolbar.ActiveTool?.ToolKind);
                HapticsStatus.Text = "Haptics: " + _haptics.Status;
            });
        };
        Presenter.StrokeInput.StrokeEnded += (_, _) => DispatcherQueue.TryEnqueue(_haptics.Stop);
        Presenter.StrokeInput.StrokeCanceled += (_, _) => DispatcherQueue.TryEnqueue(_haptics.Stop);
    }

    // ---- Modeler debug panel --------------------------------------------------------------------

    private void ConfigureModelerPanel()
    {
        InkModelerAttributes modeler = Presenter.CopyDefaultDrawingAttributes().ModelerAttributes;
        _defaultPredictionTime = modeler.PredictionTime;
        _defaultScalingFactor = modeler.ScalingFactor;
        _defaultVelocityPressure = modeler.UseVelocityBasedPressure;
        ShowModelerDefaults();
    }

    private void ShowModelerDefaults()
    {
        _updatingModelerUi = true;
        PredictionSlider.Value = _defaultPredictionTime.TotalMilliseconds;
        ScalingBox.Value = _defaultScalingFactor;
        VelocityPressureSwitch.IsOn = _defaultVelocityPressure;
        _updatingModelerUi = false;
        ModelerStatus.Text = "Platform defaults";
    }

    private void OnModelerSettingChanged(object sender, RoutedEventArgs e) => OnModelerEdited();

    private void OnScalingChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => OnModelerEdited();

    private void OnModelerEdited()
    {
        if (_updatingModelerUi)
        {
            return;
        }

        _modelerOverridden = true;
        ApplyModelerOverrides();
    }

    private void OnResetModelerClick(object sender, RoutedEventArgs e)
    {
        ShowModelerDefaults();
        _modelerOverridden = true;
        ApplyModelerOverrides();
        _modelerOverridden = false;
        ModelerStatus.Text = "Platform defaults";
    }

    private void ApplyModelerOverrides()
    {
        if (!_modelerOverridden || double.IsNaN(ScalingBox.Value))
        {
            return;
        }

        try
        {
            InkDrawingAttributes attributes = Presenter.CopyDefaultDrawingAttributes();
            InkModelerAttributes modeler = attributes.ModelerAttributes;
            modeler.PredictionTime = TimeSpan.FromMilliseconds(PredictionSlider.Value);
            modeler.ScalingFactor = (float)ScalingBox.Value;
            modeler.UseVelocityBasedPressure = VelocityPressureSwitch.IsOn;
            Presenter.UpdateDefaultDrawingAttributes(attributes);

            InkModelerAttributes applied = Presenter.CopyDefaultDrawingAttributes().ModelerAttributes;
            ModelerStatus.Text =
                $"Applied: prediction {applied.PredictionTime.TotalMilliseconds:0} ms, " +
                $"scaling {applied.ScalingFactor:0.##}, velocity pressure {(applied.UseVelocityBasedPressure ? "on" : "off")}";
        }
        catch (Exception e)
        {
            ModelerStatus.Text = "Platform rejected these values: " + e.Message;
        }
    }
}
