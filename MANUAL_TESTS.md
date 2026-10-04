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
