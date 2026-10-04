# InkBoard manual tests

Claude cannot test pen input. Run these on a pen device (Surface Pen) after each phase.
Keep OneNote (or Microsoft Whiteboard) open on the same device for the side-by-side steps.

Build and run:

```
dotnet build InkBoard.sln -c Debug -p:Platform=x64
src\InkBoard\bin\x64\Debug\net10.0-windows10.0.26100.0\InkBoard.exe
```

(Use `-p:Platform=ARM64` and the `ARM64` output folder on an ARM device.)

## Phase 1 — Scaffold + raw ink

Launch
- [ ] The app starts and shows a single white window titled "InkBoard". No crash dialog.

Input devices
- [ ] Pen draws black ink.
- [ ] A finger draws ink (touch is enabled in this phase; it becomes pan/zoom in Phase 3).
- [ ] The mouse (left button drag) draws ink.

Pressure and tilt (ballpoint, FitToCurve on)
- [ ] One stroke going from very light to very hard pressure visibly gets thicker.
- [ ] A quick flick tapers at the end.
- [ ] Tilt: write upright, then with the pen tilted almost flat. Note whether the ballpoint stroke
      changes at all. (In OneNote the ballpoint barely reacts to tilt; the pencil in Phase 2 is the real
      tilt test. Either result is fine as long as it matches OneNote.)
- [ ] A fast zig-zag is smooth (curve-fitted), with no visible polyline corners.

Wet ink is platform-rendered
- [ ] While drawing fast circles, ink stays glued to the pen tip with the same latency as OneNote.
- [ ] On pen-up there is no visible flicker, shift, or change of thickness when wet ink becomes dry ink.
- [ ] Code check: `git grep -n -i -E "PointerMoved|Polyline|PathGeometry|DrawLine" -- src/InkBoard` returns
      nothing. The app has no drawing code; the only ink code is the presenter configuration in
      `MainWindow.xaml.cs`.

Side-by-side with OneNote (same device, same pen)
- [ ] Set OneNote to a black pen of about the same thickness. Write the same word in both.
      Stroke shape, taper, pressure response and smoothness look the same.
- [ ] Draw slow and fast straight lines in both; latency feels the same.
- [ ] Resize and maximise the InkBoard window: existing ink stays put and stays sharp, new ink lands
      under the pen across the whole window.

Report back anything that differs from OneNote, however small.

## Phase 2 — OneNote-style tools

Toolbar
- [ ] The toolbar shows, left to right: ballpoint pen, pencil, highlighter, calligraphy pen, eraser, ruler/stencil.
      Ballpoint is selected at start.
- [ ] Tapping the selected pen again opens its flyout with a colour palette and a size slider.
      (The WinUI Gallery team saw empty flyouts on this experimental build. If a flyout is empty, tell me.)
- [ ] Ballpoint: pressure changes thickness.
- [ ] Pencil: graphite texture; tilting the pen flat gives a wide, soft shading stroke; pressure changes darkness.
- [ ] Highlighter: translucent, flat tip; a single stroke crossing itself does not get darker.
- [ ] Calligraphy pen: strokes are thick along one diagonal and hairline along the other (45° nib).
      Changing colour/size in its flyout takes effect.
- [ ] Eraser tool: dragging over strokes erases whole strokes. "Erase all ink" in its flyout clears the canvas.
- [ ] Ruler: appears on the canvas, can be moved with one finger and rotated with two; a pen stroke drawn
      near its edge snaps to a straight line. Protractor (from the stencil flyout): arcs snap to its edge.

Pen buttons
- [ ] Hold the barrel button and drag over strokes: they are erased. Release: the pen inks again with the
      tool that was selected. (Debug panel > Diagnostics shows "Barrel button: erasing / released".)
- [ ] The pen's tail eraser erases strokes.
- [ ] Right mouse button drag: note what it does (expected: draws ink like the left button, since
      right-drag is set to be processed). Tell me if you would rather it did something else.

Modeler debug panel ("Debug" button, top right)
- [ ] Panel opens beside the canvas and shows the platform defaults (note the three default values).
- [ ] Prediction time 0 ms vs 20 ms: at 20 the wet ink reaches further toward the pen tip during fast strokes
      (and may overshoot on sharp turns); at 0 it trails slightly more.
- [ ] Velocity-based pressure on, using the MOUSE: fast mouse strokes get thinner, slow ones thicker.
- [ ] Scaling factor: change it and note any change in stroke feel. The status line shows the values the
      platform actually accepted, or the platform's error text if it rejected them.
- [ ] Switch tool or colour after changing a value: the status line still shows your values (they are re-applied).
- [ ] "Reset to platform defaults" restores the original values.

Haptics (only with a haptic pen, e.g. Surface Slim Pen 2, with tactile signals enabled in
Settings > Bluetooth & devices > Pen & Windows Ink)
- [ ] Ballpoint stroke: pen vibrates while the tip is down and stops on pen-up.
- [ ] Pencil, highlighter and calligraphy pen each feel different from ballpoint.
- [ ] Debug panel > Diagnostics shows "Haptics: Playing waveform 0x…". With a non-haptic pen or the mouse it
      shows "Pointer is not a haptic pen" and nothing else changes.

Side-by-side with OneNote (same device, same pen)
- [ ] Pencil with tilt shading in both: texture and width response match.
- [ ] Highlighter over handwriting in both: translucency and tip shape match.
- [ ] With the debug panel at platform defaults, fast handwriting in both: latency and stroke-end behaviour
      match. Then find out whether any modeler setting makes InkBoard feel closer to, or further from, OneNote.
- [ ] Haptic feel of pen and pencil matches OneNote's.
