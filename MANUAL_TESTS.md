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
- [ ] Code check: `git grep -n -i -E "PointerMoved|Polyline|PathGeometry|DrawLine" -- src/InkBoard`.
      At Phase 1 this returned nothing. From Phase 3 on, the only matches are the lasso / point-eraser
      handlers on `InkUnprocessedInput` in `MainWindow.Tools.cs` (they select and split strokes, they do
      not draw) and the dashed `LassoLine` selection outline in `MainWindow.xaml`. No match may ever
      produce ink.

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

## Phase 3 — Canvas behaviours

Pan and zoom (canvas is 4000 x 3000, inside a ScrollViewer)
- [ ] One finger pans, two fingers pinch-zoom (25% to 800%). The zoom button (top right) shows the factor
      and resets to 100% when clicked. Mouse wheel scrolls, Ctrl + wheel zooms.
- [ ] Pen inks while "Draw with touch" is off; a resting palm or finger never leaves ink.
- [ ] "Draw with touch" on: a finger draws ink; off again: a finger pans.
- [ ] Write a word at 100%, zoom to 400%+: edges stay sharp (re-rendered, not a blurry upscaled bitmap).
      Check both right after the pinch ends and during it.
- [ ] Write while zoomed in and while zoomed out: wet ink appears exactly under the pen tip, with the
      correct thickness for the zoom level, and does not jump or change thickness on pen-up.
- [ ] Scroll to the far right/bottom of the canvas and write there: ink works across the whole canvas.
- [ ] Barrel-button erase and the ruler still work when zoomed and scrolled.

Lasso selection (dashed-square button in the toolbar)
- [ ] Drawing a loop shows a dashed blue outline that follows the pen; on pen-up a dashed box surrounds
      the enclosed strokes. Strokes outside the loop are not selected. An empty loop selects nothing.
- [ ] The outline and the box line up with the ink at 100% and when zoomed/scrolled.
      (If they are offset, tell me the zoom factor and scroll position: it means unprocessed input
      positions are not in canvas coordinates.)
- [ ] Dragging inside the box moves the selected strokes with the pen; the box follows.
- [ ] Delete key removes the selected strokes.
- [ ] Switching to another tool clears the selection.

Erasing
- [ ] Eraser tool (and barrel button, and pen tail): removes whole strokes.
- [ ] Point eraser (second eraser button): rubbing across the middle of a stroke removes only that part
      and leaves two separate strokes. Nothing is painted white: draw a highlighter stroke underneath
      first and confirm it is intact where the ink above it was erased.
- [ ] Point-erase a pencil stroke, a highlighter stroke and a calligraphy stroke: the remaining pieces keep
      their look (texture, translucency, nib angle), colour, size and pressure variation.
- [ ] Known difference to check against OneNote: the remaining pieces are rebuilt by the platform from the
      original pen points, so the cut ends are rounded stroke ends, and a curve-fitted stroke may shift
      very slightly near the cut. Say if this is visible.

Undo / redo (toolbar buttons, Ctrl+Z / Ctrl+Y)
- [ ] Draw three strokes, undo three times, redo three times.
- [ ] Erase a stroke (eraser tool), undo: it returns. Redo: it is gone.
- [ ] "Erase all ink", undo: everything returns.
- [ ] Lasso-move strokes, undo: they return to the old position. Redo: moved again.
- [ ] Lasso-delete, undo.
- [ ] Point-erase through two strokes in one gesture, undo: both originals return in one step.
- [ ] After an undo, drawing a new stroke disables redo.
- [ ] Known limitation: a stroke restored by undo comes back on top of the other strokes (z-order is not
      restored). Visible only where strokes overlap, e.g. ink under a highlighter.
- [ ] Ctrl+Z / Ctrl+Y / Delete work right after drawing with the pen (keyboard focus). If they only work
      after clicking a toolbar button, tell me.

Side-by-side with OneNote (same device, same pen)
- [ ] Zoomed to the same factor, handwriting looks equally sharp in both.
- [ ] Pinch-zoom and pan with ink on screen: ink tracks the fingers as smoothly as in OneNote.
- [ ] Stroke eraser behaviour matches OneNote's stroke eraser.
- [ ] Lasso-move a word in both: the moved ink looks identical before and after.

## Phase 4 — Persistence and OneNote interop

Everything here is under the "File" button (top left) or its shortcuts.

Save / open .isf
- [ ] Draw with every tool (ballpoint with pressure, pencil with tilt, highlighter, calligraphy), then
      File > Save ink (Ctrl+S) to a .isf file. Status text next to the undo buttons says "Saved <name>".
- [ ] Erase all, then File > Open ink (Ctrl+O): the drawing returns, and every stroke looks exactly as before
      (pressure variation, pencil texture and tilt shading, highlighter translucency, calligraphy nib).
- [ ] Ctrl+Z after opening restores what was on the canvas before the file was opened.
- [ ] Opening a file that is not ink (rename a .txt to .isf) shows an error in the status text and leaves
      the canvas untouched.

Clipboard inside InkBoard
- [ ] Lasso some strokes, Ctrl+C, then Ctrl+V: a copy appears near the top-left of the visible area, selected
      (dashed box) with the lasso tool active, and can be dragged straight away.
- [ ] Ctrl+X removes the selection; Ctrl+V brings it back. Undo works for both.
- [ ] Ctrl+C with nothing selected: status text says to select ink first.

OneNote round trip (same device)
- [ ] InkBoard to OneNote: lasso a handwritten word with strong pressure variation, Ctrl+C, paste into
      OneNote. It arrives as ink (lasso-selectable, erasable stroke by stroke), not as a picture, with the
      same pressure variation, colour and thickness.
- [ ] OneNote to InkBoard: lasso ink in OneNote (pen, pencil and highlighter strokes), copy, Ctrl+V in
      InkBoard. It arrives as strokes with pressure intact; the point eraser and stroke eraser work on it.
- [ ] Full circle: InkBoard to OneNote, back to InkBoard, next to the original: the two look identical.
- [ ] Note anything OneNote changes (for example pencil or calligraphy strokes arriving as plain pen):
      that would be an ISF/clipboard format limit, not something the app can fix in app code.

Export PNG
- [ ] File > Export PNG: the image has a white background, is cropped to the ink with a small margin, and
      is sharp (rendered at 192 DPI).
- [ ] Compare the PNG with the canvas at 200% zoom: ballpoint, highlighter and calligraphy strokes match.
      Look closely at PENCIL strokes: the export uses the OS dry-ink renderer (IInkD2DRenderer); tell me if
      the pencil texture differs from what the InkCanvas shows. This matters for Phase 6.
- [ ] Export with an empty canvas: status text says "Nothing to export".

Side-by-side with OneNote
- [ ] Open the exported PNG next to the same ink pasted into OneNote at the same zoom: strokes look the same.

## Phase 5 — Recognition

Both commands are under "Convert" (top right) and act on the lasso selection.

Ink to text
- [ ] Write a word, lasso it, Convert > Ink to text: the ink is replaced by typed text at the same place and
      roughly the same size; the status text shows what was recognised.
- [ ] Two handwritten lines selected together become two lines of text.
- [ ] Ctrl+Z brings the ink back and removes the text; Ctrl+Y converts again.
- [ ] With nothing selected: status text says to select ink first. With a scribble: "No handwriting recognised".
- [ ] Recognition language follows the Windows handwriting language; check a word in your usual language.
- [ ] Known limits of this sample: the text box cannot be moved, edited, erased or saved to .isf.

Ink to shape
- [ ] Draw a rough rectangle, a rough ellipse/circle and a roughly straight line. Lasso all three,
      Convert > Ink to shape: each is replaced by a clean stroke (sharp-cornered rectangle, smooth ellipse,
      straight line) in the same colour, thickness and tool. Status text lists what was converted.
- [ ] A rectangle drawn at an angle stays at that angle.
- [ ] Handwriting, triangles and scribbles in the same selection are left untouched.
- [ ] The clean shapes are real ink: the stroke eraser and point eraser work on them, lasso moves them,
      they survive save/open and copy/paste into OneNote.
- [ ] One Ctrl+Z restores all the hand-drawn originals.
- [ ] Note: lines are detected by the app (a single stroke that stays close to the straight line between
      its ends), because InkAnalyzer has no "line" shape. Tell me if it is too eager or too strict.

Side-by-side with OneNote
- [ ] Convert the same handwritten sentence with OneNote's "Ink to Text" and with InkBoard: the recognised
      text is the same.
- [ ] Convert the same rough rectangle and ellipse with OneNote's "Ink to Shape" and with InkBoard:
      compare how clean the result is and whether the stroke looks like the same pen.

## Phase 7 — Ink and highlighter colours

- [ ] Tap the selected ballpoint, pencil or calligraphy pen again: the flyout shows 22 colours (black and
      greys, reds, orange, gold, browns, greens, teal, blues, navy, purple, lavender, pinks) and a size slider.
- [ ] Tap the selected highlighter again: 9 highlighter colours (yellow, green, turquoise, pink, orange,
      lavender, red, blue, grey).
- [ ] Each tool remembers its own colour and size when you switch tools and come back.
- [ ] Side-by-side with OneNote: pick the matching pen colours and the yellow/green/pink highlighters in
      both; tell me any colour you use in OneNote that is missing or looks off.

## Phase 6 — Custom drying (parked)

Not built yet. It starts only after you confirm Phases 1-5 pass the pen tests above.
