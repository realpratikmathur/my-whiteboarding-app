using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using InkBoard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using InkDrawingAttributes = Windows.UI.Input.Inking.InkDrawingAttributes;
using InkDrawingAttributesKind = Windows.UI.Input.Inking.InkDrawingAttributesKind;
using InkStroke = Windows.UI.Input.Inking.InkStroke;
using InkStrokeBuilder = Windows.UI.Input.Inking.InkStrokeBuilder;

namespace InkBoard;

// "Start: ghost page" for replay.
//
// When the replay starts, the finished lesson is put on the page in the private-notes colour, which the OBS
// filter removes from the video: the presenter sees the whole lesson, the audience an empty page. Each
// ghost goes away as the real ink or text replaces it, so the real content always replays over its ghost.
//
// Ghost strokes are ordinary platform strokes, built by InkStrokeBuilder from the recorded stroke's own ink
// points, with the private colour and half the width. Half the width matters: as the real stroke is drawn
// over its ghost it covers it completely, so no magenta shows at its anti-aliased edges (a blend of ink and
// magenta is neither, and the filter could not remove it). Pencil strokes are see-through,
// so their ghost is removed the moment their replay starts instead. Highlighter strokes get no ghost at
// all: a highlighter usually lies over writing that replays before it, and that writing would then be drawn
// over a magenta band, leaving magenta-blended edges in the video (and hiding the ghost writing under it).
public sealed partial class MainWindow
{
    private const float GhostWidthFactor = 0.5f;

    private readonly Dictionary<int, InkStroke> _ghostStrokes = [];       // recording key -> ghost on the page
    private readonly Dictionary<Guid, PageObject> _ghostTexts = [];      // recorded text id -> its ghost

    private ReplayStartMode StartMode =>
        StartBox.SelectedIndex == 1 ? ReplayStartMode.Ghost : ReplayStartMode.Blank;

    /// <summary>Remembered for next time; changing it while replaying starts the replay over.</summary>
    private void OnStartModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StartBox.SelectedIndex < 0)
        {
            return;
        }

        if (_notebook.ReplayStart != StartMode && !string.IsNullOrEmpty(_notebookFolder) && _notebookReady.Task.IsCompleted)
        {
            _notebook.ReplayStart = StartMode;
            SaveNotebookIndex();
        }

        if (_replaying)
        {
            RestartReplay();
        }
    }

    /// <summary>
    /// Puts the finished lesson on the page as a ghost in the private colour. When rewinding to a moment
    /// (<paramref name="at"/>), ghosts that would already be gone by then are left out instead of being added
    /// and removed again: every change makes the ink layer redraw the page, which is slow on a full page.
    /// </summary>
    private void ShowGhostPage(double at = -1)
    {
        RecordingState end = RecordingState.AtEnd(_recording);
        (HashSet<int> goneStrokes, HashSet<Guid> goneTexts) = at >= 0 ? GhostsGoneBy(at) : ([], []);
        var ghosts = new List<InkStroke>();
        foreach (int key in end.Strokes.Where(InRecording).Order())
        {
            InkStroke source = _recordedList[key];
            if (source.DrawingAttributes.DrawAsHighlighter || goneStrokes.Contains(key))
            {
                continue;
            }

            (double dx, double dy) = end.Offsets.GetValueOrDefault(key);
            Matrix3x2 transform = source.PointTransform * Matrix3x2.CreateTranslation((float)dx, (float)dy);

            var builder = new InkStrokeBuilder();
            builder.SetDefaultDrawingAttributes(GhostAttributes(source.DrawingAttributes));
            InkStroke ghost = builder.CreateStrokeFromInkPoints(source.GetInkPoints(), transform);
            ghosts.Add(ghost);
            _ghostStrokes[key] = ghost;
        }

        if (_offline)
        {
            _strokeAdds!.AddRange(ghosts);   // first, so the real ink of the rewind goes on top of them
        }
        else
        {
            _document.Container.AddStrokes(ghosts);
        }
   // one call to the ink thread instead of one per stroke

        foreach (PageObject text in end.Objects.Values.Where(o =>
                     o.Kind == PageObjectKind.Text && !string.IsNullOrWhiteSpace(o.Text) && !goneTexts.Contains(o.Id)))
        {
            PageObject ghost = text.Clone();
            ghost.Id = Guid.NewGuid();
            ghost.Color = PrivateInk.Hex;
            if (!_offline)
            {
                ShowTextGhost(ghost);
            }

            _ghostTexts[text.Id] = ghost;
        }
    }

    /// <summary>
    /// The ghosts that replaying up to a moment removes (same rules as Begin and Draw): a stroke that appears
    /// at once, a see-through stroke whose drawing has started, any other stroke whose drawing has finished,
    /// and a text whose box has appeared.
    /// </summary>
    private (HashSet<int> Strokes, HashSet<Guid> Texts) GhostsGoneBy(double position)
    {
        var strokes = new HashSet<int>();
        var texts = new HashSet<Guid>();
        foreach (ReplayTimeline.Item item in _timeline.Items)
        {
            if (item.Start > position)
            {
                break;   // items are in start order
            }

            RecordedEvent e = item.Event;
            if (e.Kind == RecordedEventKind.StrokeAdd)
            {
                bool drawn = e.Duration > 0 && e.Strokes.Count == 1 && InRecording(e.Strokes[0]);
                if (!drawn)
                {
                    strokes.UnionWith(e.Strokes);
                }
                else if (item.End <= position || IsSeeThrough(_recordedList[e.Strokes[0]]))
                {
                    strokes.Add(e.Strokes[0]);
                }
            }
            else if (e.Kind == RecordedEventKind.ObjectAdd)
            {
                texts.Add(e.ObjectId);
            }
        }

        return (strokes, texts);
    }

    /// <summary>Puts a text ghost on the page; it cannot be clicked or typed into.</summary>
    private void ShowTextGhost(PageObject ghost)
    {
        ShowObject(ghost);
        if (_objectViews.TryGetValue(ghost, out FrameworkElement? view) && view is TextBox box)
        {
            box.IsReadOnly = true;
            box.IsHitTestVisible = false;
            box.IsTabStop = false;
        }
    }

    /// <summary>Same tip, shape and pressure behaviour as the original, in the private colour, half as wide.</summary>
    private static InkDrawingAttributes GhostAttributes(InkDrawingAttributes source)
    {
        var ghost = new InkDrawingAttributes { Color = PrivateInk.Color };
        Size size = source.Size;
        if (source.Kind == InkDrawingAttributesKind.Pencil)
        {
            // A plain round pen: pencil texture would make the ghost speckled and hard to read.
            ghost.Size = new Size(Math.Max(1, size.Width * GhostWidthFactor), Math.Max(1, size.Height * GhostWidthFactor));
            return ghost;
        }

        ghost.PenTip = source.PenTip;
        ghost.PenTipTransform = source.PenTipTransform;
        ghost.FitToCurve = source.FitToCurve;
        ghost.IgnorePressure = source.IgnorePressure;
        ghost.IgnoreTilt = source.IgnoreTilt;
        ghost.Size = new Size(Math.Max(1, size.Width * GhostWidthFactor), Math.Max(1, size.Height * GhostWidthFactor));
        return ghost;
    }

    /// <summary>True for strokes the ghost would show through while they are drawn over it.</summary>
    private static bool IsSeeThrough(InkStroke stroke)
    {
        InkDrawingAttributes attributes = stroke.DrawingAttributes;
        return attributes.DrawAsHighlighter || attributes.Kind == InkDrawingAttributesKind.Pencil;
    }

    /// <summary>While seeking, ghosts to remove are collected and removed together (see SeekReplay).</summary>
    private List<InkStroke>? _ghostRemovals;

    private void RemoveGhost(int key)
    {
        if (!_ghostStrokes.Remove(key, out InkStroke? ghost))
        {
            return;
        }

        if (_offline)
        {
            _strokeAdds!.Remove(ghost);   // never put on the page
            return;
        }

        if (_ghostRemovals is not null)
        {
            _ghostRemovals.Add(ghost);
            return;
        }

        // Nothing else stays selected during replay (every selection there is made and deleted or moved at
        // once), so selecting just the ghost is enough.
        ghost.Selected = true;
        _document.Container.DeleteSelected();
    }

    private void FlushGhostRemovals()
    {
        List<InkStroke>? ghosts = _ghostRemovals;
        _ghostRemovals = null;
        if (ghosts is { Count: > 0 })
        {
            foreach (InkStroke ghost in ghosts)
            {
                ghost.Selected = true;
            }

            _document.Container.DeleteSelected();
        }
    }

    private void RemoveTextGhost(Guid textId)
    {
        if (_ghostTexts.Remove(textId, out PageObject? ghost))
        {
            HideObject(ghost);
        }
    }

    private void ClearGhosts()
    {
        _ghostRemovals = [];
        foreach (int key in _ghostStrokes.Keys.ToList())
        {
            RemoveGhost(key);
        }

        FlushGhostRemovals();   // one delete for all of them

        foreach (Guid id in _ghostTexts.Keys.ToList())
        {
            RemoveTextGhost(id);
        }
    }
}
