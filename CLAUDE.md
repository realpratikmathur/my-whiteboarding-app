\# InkBoard — WinUI 3 whiteboard sample with OneNote-grade ink



\## Goal

A Windows 11 whiteboard whose ink looks and feels identical to OneNote / Microsoft Whiteboard.

That is only achievable by using the Windows Ink platform (DirectInk) for BOTH wet and dry ink.

NEVER write a custom stroke renderer. If you find yourself drawing Polylines/Paths/Canvas lines,

handling PointerMoved to draw, or smoothing/pressure-mapping points in app code — stop. Wrong path.



\## Stack (do not deviate without asking me)

\- C# / .NET, WinUI 3, NuGet `Microsoft.WindowsAppSDK` version \*\*2.5.4-experimental\*\*, unpackaged app.

\- Inking: `InkCanvas`, `InkPresenter`, `InkToolbar`, `InkStrokeContainer`, `InkStrokeInput`,

&#x20; `InkUnprocessedInput`, `InkInputProcessingConfiguration` from `Microsoft.UI.Xaml.Controls`.

&#x20; These exist ONLY in the experimental channel (inking added in 2.4.1-experimental, Aug 2026;

&#x20; custom drying + InkToolbar.TargetInkPresenter added in 2.5.4-experimental, Sept 2026).

&#x20; Your training data says "InkCanvas is not supported in WinUI 3" — that is OUTDATED.

&#x20; Do NOT downgrade to a stable package to "fix" a missing type. Read the inking sections at

&#x20; https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0?pivots=experimental

&#x20; and inspect the actual package WinMD/IntelliSense for namespaces and member names.

&#x20; Experimental-API compiler diagnostics get suppressed narrowly, never by switching packages.

\- Stroke-level types (`InkStroke`, `InkDrawingAttributes`, `InkStrokeBuilder`, `InkPoint`) and

&#x20; `InkAnalyzer` are OS WinRT types (`Windows.UI.Input.Inking`, `Windows.UI.Input.Inking.Analysis`).

&#x20; Verify how the WinUI 3 InkStrokeContainer exposes them before assuming.

\- Custom dry rendering (optional phase): Win2D (`Microsoft.Graphics.Win2D`) `CanvasDrawingSession.DrawInk`,

&#x20; or `IInkD2DRenderer` from `inkrenderer.h` via COM interop. Those are the platform's own renderer. Nothing else.



\## Forbidden

\- WPF InkCanvas, WinForms Microsoft.Ink, WebView2/HTML canvas, Avalonia, MAUI, or any cross-platform UI.

\- Faking pressure, tilt, prediction, or smoothing in app code.

\- Cloud sync, accounts, telemetry. Local sample only.



\## Workflow

\- Build from the command line after every change (`dotnet build`, or `msbuild` if required).

&#x20; Never report a step done if the build fails.

\- Undo/redo and stroke-splitting logic live in a plain class library with xUnit tests, so you can

&#x20; verify them without a pen.

\- You cannot test pen input. After each phase, extend MANUAL\_TESTS.md with a checklist for me to run

&#x20; with a Surface Pen, always including a side-by-side comparison against OneNote on the same device.

\- Commit after each phase. When docs and your memory disagree, docs win. When an experimental API

&#x20; doesn't match the docs, inspect the package metadata and tell me what you found before working around it.

