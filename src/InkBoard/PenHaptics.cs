using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Haptics;
using Windows.Devices.Input;

namespace InkBoard;

/// <summary>
/// Plays the pen's built-in inking waveform for the active tool while a stroke is in progress.
/// Does nothing on pens without haptics.
/// </summary>
public sealed class PenHaptics
{
    private SimpleHapticsController? _active;

    /// <summary>Last outcome, for the debug panel.</summary>
    public string Status { get; private set; } = "No stroke yet";

    public void Start(uint pointerId, InkToolbarTool? tool)
    {
        Stop();
        try
        {
            SimpleHapticsController? controller = PenDevice.GetFromPointerId(pointerId)?.SimpleHapticsController;
            if (controller is null)
            {
                Status = "Pointer is not a haptic pen";
                return;
            }

            ushort waveform = WaveformFor(tool);
            SimpleHapticsControllerFeedback? feedback =
                controller.SupportedFeedback.FirstOrDefault(f => f.Waveform == waveform)
                ?? controller.SupportedFeedback.FirstOrDefault(f => f.Waveform == KnownSimpleHapticsControllerWaveforms.InkContinuous);
            if (feedback is null)
            {
                Status = "Pen has haptics but no inking waveform";
                return;
            }

            controller.SendHapticFeedback(feedback);
            _active = controller;
            Status = $"Playing waveform 0x{feedback.Waveform:X4}";
        }
        catch (Exception e)
        {
            Status = "Haptics failed: " + e.Message;
        }
    }

    public void Stop()
    {
        try
        {
            _active?.StopFeedback();
        }
        catch (Exception e)
        {
            Status = "Haptics stop failed: " + e.Message;
        }
        _active = null;
    }

    private static ushort WaveformFor(InkToolbarTool? tool) => tool switch
    {
        InkToolbarTool.Pencil => KnownSimpleHapticsControllerWaveforms.PencilContinuous,
        InkToolbarTool.Highlighter => KnownSimpleHapticsControllerWaveforms.MarkerContinuous,
        InkToolbarTool.CustomPen => KnownSimpleHapticsControllerWaveforms.BrushContinuous,
        InkToolbarTool.Eraser => KnownSimpleHapticsControllerWaveforms.EraserContinuous,
        _ => KnownSimpleHapticsControllerWaveforms.InkContinuous,
    };
}
