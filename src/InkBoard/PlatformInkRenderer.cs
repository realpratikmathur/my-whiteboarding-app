using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Windows.UI.Input.Inking;
using WinRT;

namespace InkBoard;

/// <summary>
/// Draws dry ink with the OS ink renderer, <c>IInkD2DRenderer</c> from inkrenderer.h, into a Win2D drawing
/// session. Win2D for WinUI 3 (Microsoft.Graphics.Win2D 1.4.0) has no CanvasDrawingSession.DrawInk, so
/// this calls the same COM renderer DrawInk wraps on UWP. Win2D only supplies the Direct2D device context.
/// </summary>
internal static class PlatformInkRenderer
{
    private static readonly Guid ClsidInkD2DRenderer = new("4044e60c-7b01-4671-a97c-04e0210a07a5");
    private static readonly Guid IidD2D1DeviceContext = new("e8f7fe7a-191c-466d-ad95-975678bda998");

    [ComImport, Guid("407fb1de-f85a-4150-97cf-b7fb274fb4f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInkD2DRenderer
    {
        void Draw(IntPtr d2d1DeviceContext, IntPtr inkStrokeIterable, [MarshalAs(UnmanagedType.Bool)] bool highContrast);
    }

    // Microsoft.Graphics.Canvas.native.h
    [ComImport, Guid("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICanvasResourceWrapperNative
    {
        void GetNativeResource(IntPtr canvasDevice, float dpi, in Guid iid, out IntPtr resource);
    }

    /// <summary>Draws the strokes using the session's current transform and DPI.</summary>
    public static void Draw(CanvasDrawingSession session, IEnumerable<InkStroke> strokes)
    {
        var renderer = (IInkD2DRenderer)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ClsidInkD2DRenderer, throwOnError: true)!)!;

        IntPtr deviceContext = IntPtr.Zero, iterable = IntPtr.Zero;
        try
        {
            var native = (ICanvasResourceWrapperNative)Marshal.GetObjectForIUnknown(
                ((IWinRTObject)session).NativeObject.ThisPtr);
            native.GetNativeResource(IntPtr.Zero, 0, IidD2D1DeviceContext, out deviceContext);

            iterable = MarshalInterface<IEnumerable<InkStroke>>.FromManaged(strokes);
            renderer.Draw(deviceContext, iterable, highContrast: false);
        }
        finally
        {
            if (iterable != IntPtr.Zero)
            {
                Marshal.Release(iterable);
            }

            if (deviceContext != IntPtr.Zero)
            {
                Marshal.Release(deviceContext);
            }

            Marshal.ReleaseComObject(renderer);
        }
    }
}
