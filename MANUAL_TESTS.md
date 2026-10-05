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

## Phase 8 — Endless page

- [ ] Scroll (finger, wheel or scrollbar) to the bottom and keep going: the page keeps extending downward;
      you never hit an end. Same to the right.
- [ ] Write near the bottom edge of the visible area, scroll down, keep writing, repeat many times: ink
      always lands under the pen and old ink stays where it was.
- [ ] After going far down (tens of screens), zoom in and out and write: still crisp, same latency as at
      the top of the page.
- [ ] Lasso, point eraser, ruler and undo work far down the page.
- [ ] Export PNG of a very tall page: if it fails, tell me the status text (very tall images can exceed
      the graphics card's bitmap limit).
- [ ] Side-by-side with OneNote: writing a long page while scrolling down feels the same.

## Phase 9 — Typed text, pictures and PDFs

The "A" button in the toolbar is the Text and pictures tool.

Typed text
- [ ] Select the tool and tap an empty spot (pen, finger with Draw with touch, or mouse): a caret appears;
      type a few lines with Enter. Text is black on the white page with no box around it.
- [ ] Tap elsewhere: the text stays; a new caret starts at the new spot. Tapping without typing leaves nothing.
- [ ] Tap an existing text: it gets a thin blue box and you can keep typing at its end. Drag it to move it.
- [ ] Switch back to a pen and write over and around the text: ink is drawn above it.
- [ ] Known limit: text is edited with the keyboard only (arrow keys, Shift+arrows); you cannot place the
      caret or select words with the pen/mouse, because the ink layer owns pointer input.
- [ ] Ink to text (Convert menu) now produces the same kind of text box: it can be moved and edited.

Pictures
- [ ] Copy a picture (Snipping Tool, a browser image) and Ctrl+V: it appears near the top-left of the view.
- [ ] Copy an image file in File Explorer and Ctrl+V; also File > Insert picture.
- [ ] With the Text and pictures tool: tap the picture (blue box + white square handle), drag to move, drag
      the square handle to resize (keeps proportions), Delete to remove.
- [ ] Draw and highlight over the picture with pen and highlighter; erase the ink again: the picture is untouched.

PDFs
- [ ] File > Insert PDF with a multi-page PDF (or copy a PDF file in Explorer and Ctrl+V): every page appears
      as a picture, stacked downward, sharp when zoomed to 200%. The page grows to fit a long PDF.
- [ ] Annotate the PDF pages with ink; scroll, zoom, lasso the ink.

Undo
- [ ] Ctrl+Z / Ctrl+Y step through: adding text, editing text, pasting/inserting pictures (one step per PDF
      page), moving, resizing, deleting.
- [ ] While the caret is in a text box, Ctrl+Z undoes typing inside that box only.

Side-by-side with OneNote
- [ ] Insert the same PDF as a printout in OneNote and annotate both: ink sits on the pages the same way.

## Phase 10 — Pages, subpages, auto-save

Your notebook lives in `Documents\InkBoard`. The list button (top left) shows/hides the page list.

Pages
- [ ] First start: one page, "Page 1". "+ Page" adds a page at the end and opens it (empty).
- [ ] "+ Subpage" adds a subpage under the open page, indented; a subpage can have its own subpage
      (three levels, like OneNote); a fourth level is refused with a message.
- [ ] Tap pages in the list to switch: each shows its own ink, text and pictures. Undo history starts fresh
      on each switch.
- [ ] "…" > Rename, Move up, Move down, Make subpage, Promote behave as in OneNote's page list.
- [ ] "…" > Delete asks for confirmation, then removes the page and its subpages. Deleting the last page
      leaves a fresh "Page 1".

Auto-save and auto-load
- [ ] Draw, type and paste on two or three pages. Close the window with the X. Start InkBoard again: the
      same page is open, with everything on it, and the other pages are intact (pressure, pencil texture,
      highlighter, text, pictures, PDF pages, page length).
- [ ] Draw something and, without doing anything else, wait about 3 seconds, then end the app from Task
      Manager. Start again: the drawing is there (auto-save runs 2 seconds after the last change).
- [ ] File > Export ink / Import ink still write and read a single .isf file for the open page.

Side-by-side with OneNote
- [ ] Build the same small page/subpage tree in both; switching pages should feel as quick.

## Phase 11 — Record and replay

Recording
- [ ] Press Record (red dot, top right): the button shows "Stop 0:00" counting up. Write a few lines with the
      pen, pausing between them; switch colour and tool; type a text box; paste a picture; erase a stroke;
      lasso-move a word; press Ctrl+Z once. Press the button again to stop.
- [ ] Record a second take on the same page later (even after restarting the app): it continues the same
      lesson. Things you changed while NOT recording appear at once at the start of the next take.
- [ ] File > Clear this page's recording asks first, then the Replay button is disabled again.
- [ ] Switching to another page stops the recording. Each page has its own recording.

Replay
- [ ] Press Replay: a bar appears (Play, Restart, speed, progress, time, Exit replay), the page is emptied
      and the tools are greyed out.
- [ ] Press Play: every stroke is drawn again as you wrote it - same order, same rhythm within a stroke,
      same pen, colour, pressure and pencil/highlighter look. Text appears as it was typed, pictures appear
      when they were pasted, erased and moved strokes disappear/move at the right moment.
- [ ] Pauses in your lesson longer than 2 seconds last 2 seconds in the replay.
- [ ] The view follows the writing: it stays still while the pen is near the middle of the screen, glides
      when the writing leaves the middle, and scrolls down the page as the lesson goes down. When the lesson
      jumps to a far part of the page, the view gets there just before the writing starts.
- [ ] Pause freezes everything mid-stroke; Play continues from the same point.
- [ ] Speed 0.5x / 1x / 1.5x / 2x / 2.5x can be changed while playing.
- [ ] Restart goes back to the empty page. Play at the end starts over.
- [ ] Zoom before pressing Replay (for example 150%): replay keeps that zoom and still follows.
- [ ] Exit replay: the page is exactly as it was before, at the same scroll position, and editing works.
- [ ] Look closely at a stroke WHILE it is being redrawn: it is real ink (the platform renders the growing
      stroke). With curve-fitted ink the tail may wiggle very slightly as it grows; tell me if it distracts.
- [ ] A long lesson (10+ minutes, hundreds of strokes): replay stays smooth. Tell me if very long single
      strokes stutter.

Side-by-side with OneNote
- [ ] OneNote's Ink Replay on similar handwriting: InkBoard's replay should look at least as natural.

## Phase 12 — Private notes hidden from OBS

Private notes are pure magenta (#FF00FF). They are for your eyes only: OBS removes them from the video,
they are never recorded, and during replay they stay on the page the whole time.

In the app
- [ ] The padlock pen in the toolbar writes magenta; its flyout offers only that colour and a size.
- [ ] "Private text" (top right) on: new text boxes are magenta. Off: black again.
- [ ] No other pen or highlighter palette contains pure magenta.
- [ ] Record a lesson in which you also write private notes (pen and text). Replay: the private notes are on
      the page from the first moment and never move or disappear, while the regular ink plays back.
- [ ] Erase or move a private note while recording: nothing about it shows up in the replay.

OBS setup (once)
1. In OBS add a Window Capture source for InkBoard (Capture method: Windows 10 (1903 and up)).
   In the source's properties tick "Force SDR". This matters on your HDR monitors: without it OBS
   tone-maps the capture, colours shift and the filter cannot match them.
2. Right-click the source > Filters > "+" under Effect Filters > "Apply LUT".
3. Path: `obs\InkBoard-hide-private-ink.png` from this repo (or File > Save OBS filter file in InkBoard
   to write a fresh copy anywhere). Amount: 1.0.
That is all: no colour key, no chroma key, no extra colour source.

Check in OBS
- [ ] In the OBS preview the private notes are gone and the page is plain white where they are; every other
      ink colour, pencil, highlighter, text, picture and the toolbar look unchanged.
- [ ] Record a short clip while replaying a lesson with private notes on the page, then play the file:
      regular ink appears as it replays, private notes never appear. Compare with
      `obs\verification-without-filter.png` and `obs\verification-with-filter.png` (frames from my test).
- [ ] Zoom the page to 200% and 50% and check the preview again (edge pixels change with zoom).
- [ ] With the real pen: write private notes thin and thick, fast and slow; look for any faint magenta
      outline in the recording. If you see one, tell me the zoom level and pen size.

Known limits
- Keep private notes on blank page. Where a private note crosses regular ink, a picture or a PDF, the
  video shows white there instead of what is underneath (the filter turns magenta into white).
- The filter also removes pure magenta, and only pure magenta and its pale tints, inside pictures and PDFs.
- If you turn the filter off or record with another tool, the notes are visible.
- Display capture or a different capture method must also deliver unmodified SDR colours; if OBS shows the
  page greyish instead of white, "Force SDR" is not active.

## Phase 13 — Dialogs, right-click page menu, closing the window

Dialogs (the bug: clicks on a dialog were inked onto the page instead)
- [ ] "…" > Rename: click in the text field, type, click Rename and Cancel with the PEN and with the MOUSE.
      The buttons respond and no ink appears on the page behind the dialog.
- [ ] "…" > Delete and File > Clear this page's recording: same check for both buttons.
- [ ] While a dialog is open, try to draw on the page next to it: nothing is drawn.
- [ ] After the dialog closes, the pen draws again straight away; with "Draw with touch" on, touch draws
      again too; during replay the page stays read-only.
- [ ] If ink can still be drawn over a dialog, tell me: I will then move these confirmations into pop-up
      menus, which have their own window.

Right-click menu on page names
- [ ] Right-click a page in the list (or press and hold with the pen): New subpage, Rename…, Move up,
      Move down, Make subpage, Promote, Delete….
- [ ] The commands act on the page you right-clicked, even when another page is open; the open page stays
      open (except "New subpage", which opens the new page, and deleting the open page).
- [ ] Items that cannot apply are greyed out: Promote on a top-level page, Move up on the first page of a
      level, Move down on the last, Make subpage on the first, New subpage on a third-level page.
- [ ] Right-click the empty area below the pages: "New page".
- [ ] Delete another page while the open page has fresh ink: the open page keeps that ink.

Closing
- [ ] The window's X closes the app (it did not before), and what you drew a moment earlier is there on
      the next start.

## Phase 14 — Mouse wheel zoom and middle-button pan

What I could and could not check: the wheel was tested with real wheel messages sent to the app's own
window and zooms correctly. Mouse BUTTONS cannot be simulated that way, so the middle-button pan is
untested with a real mouse; please check it first.

Middle-button pan
- [ ] With any pen tool selected: press and hold the middle mouse button (wheel click) over the page and
      drag. The page moves with the mouse, in every direction, and stops when you release. No ink is drawn.
- [ ] Same with the eraser, point eraser, lasso and Text tools selected: it pans; it does not erase, start a
      lasso, or create a text box.
- [ ] Pan while zoomed in (300%) and zoomed out (50%): the point you grabbed stays under the cursor.
- [ ] If the middle button draws ink or does nothing, tell me which tool was selected: it means the ink
      layer handles that button differently from what I assumed, and I will switch approach.

Wheel zoom
- [ ] Wheel up zooms in, wheel down zooms out, about 10% per notch, between 25% and 800%. The zoom button
      (top right) shows the factor.
- [ ] The point under the cursor stays put while zooming (zoom in on a word: it stays under the cursor).
- [ ] Ctrl + wheel scrolls the page up/down; Shift + wheel scrolls left/right.
- [ ] Ink stays sharp after zooming with the wheel, and the pen writes exactly under its tip afterwards.
- [ ] Touch still pans with one finger and pinch-zooms; the pen still inks.
- [ ] Note for a laptop touchpad: a two-finger scroll is a wheel to Windows, so it now zooms. Tell me if
      you would rather have touchpad scrolling pan the page.

Side-by-side with OneNote
- [ ] Compare the feel of zooming and of middle-button navigation; say if the zoom step should be finer
      or coarser.

## Phase 15 — Middle-button pan fixed; tools that depend on ink-layer events repaired

Everything in the first two blocks was verified with real mouse input (the `realmouse` self-test drives
the app with actual mouse events). The pen-only items in the last block could not be.

Middle-button pan (the bug: it drew ink)
- [ ] Hold the middle mouse button and drag: the page moves with the mouse and nothing is drawn - with the
      pen tools, the eraser (nothing is erased), the point eraser, the lasso and the text tool.
- [ ] Works the same when zoomed in or out; the page follows the mouse one to one.
- [ ] Middle-clicking outside the page (toolbar, page list, another app) behaves as it always did.

Tools that had not been receiving real input (found while fixing the above)
- [ ] Lasso with the mouse and with the pen: draw a loop around ink, the dashed box appears; drag inside it to
      move; Delete removes. Also when zoomed in.
- [ ] Point eraser with mouse and pen: cuts a stroke where you rub.
- [ ] Text tool: click/tap an empty spot and type; click a text or picture to select, drag to move.
- [ ] Right mouse button drag over strokes erases whole strokes; Ctrl+Z brings them back.

Pen-only (I cannot test these; please check and tell me exactly what happens)
- [ ] Pen barrel button held while dragging over strokes: erases whole strokes (same path as the right mouse
      button). Releasing the button: the pen inks again.
- [ ] Pen tail eraser: erases strokes.
- [ ] Lasso / point eraser / text tool with the PEN land exactly under the pen tip (positions now come from
      the system cursor, which normally follows the pen). If they are offset or do nothing with the pen,
      tell me.
- [ ] Haptic pen: vibration while inking (Debug panel shows the haptics status). This depends on the app
      seeing the pen hover before it touches down; it may not work on this experimental build.
- [ ] With "Draw with touch" on: a finger inks. Lasso / point eraser / text tool with a FINGER are not
      expected to work (the cursor does not follow touch); use pen or mouse for those.

## Phase 16 — Replay follow mode: centred or nudge at edges

Press Replay: the bar now has a "Follow" box next to the speed. Choose before pressing Play (it can also be
changed while playing). The choice is remembered for next time.

Follow: nudge at edges
- [ ] Replay a lesson written across and down a screenful. The view stays completely still while the
      writing is inside the screen.
- [ ] When the writing reaches the last line at the bottom (about the bottom seventh of the screen), the
      view eases down by roughly a tenth of the screen so that line and the next ones are visible, then is
      still again.
- [ ] When a line runs toward the right edge, the view eases right the same way; when the next line starts
      back at the left, it eases back.
- [ ] Every movement starts and stops gently - no jumps. A return from the far right to the start of the next
      line is the largest movement; say if it should be slower.
- [ ] The ink being written is always on screen, also at 0.5x and 2.5x speed and when zoomed in.
- [ ] A lesson that jumps to a far part of the page: the view travels there shortly before the writing starts.
- [ ] Private notes behave as before (always on the page, never replayed).

Follow: centred
- [ ] Same lesson: the writing is kept around the middle of the screen, as before.

Both
- [ ] Switch between the two while a replay is running: it takes effect at once.
- [ ] Close and restart the app: the Follow box shows your last choice.
- [ ] For recording with OBS, compare both and tell me which you prefer as the default and whether the
      trigger zones (bottom ~14%, right ~10%) or the nudge distance should change.

## Phase 6 — Custom drying (parked)

Not built yet. It starts only after you confirm Phases 1-5 pass the pen tests above.
