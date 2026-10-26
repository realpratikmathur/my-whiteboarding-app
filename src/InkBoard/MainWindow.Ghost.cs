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

    /// <summary>Puts the finished lesson on the page as a ghost in the private colour.</summary>
    private void ShowGhostPage()
    {
        RecordingState end = RecordingState.AtEnd(_recording);
        foreach (int key in end.Strokes.Where(InRecording).Order())
        {
            InkStroke source = _recordedList[key];
            if (source.DrawingAttributes.DrawAsHighlighter)
            {
                continue;
            }

            (double dx, double dy) = end.Offsets.GetValueOrDefault(key);
            Matrix3x2 transform = source.PointTransform * Matrix3x2.CreateTranslation((float)dx, (float)dy);

            var builder = new InkStrokeBuilder();
            builder.SetDefaultDrawingAttributes(GhostAttributes(source.DrawingAttributes));
            InkStroke ghost = builder.CreateStrokeFromInkPoints(source.GetInkPoints(), transform);
            _document.Container.AddStroke(ghost);
            _ghostStrokes[key] = ghost;
        }

        foreach (PageObject text in end.Objects.Values.Where(o => o.Kind == PageObjectKind.Text && !string.IsNullOrWhiteSpace(o.Text)))
        {
            PageObject ghost = text.Clone();
            ghost.Id = Guid.NewGuid();
            ghost.Color = PrivateInk.Hex;
            ShowObject(ghost);
            if (_objectViews.TryGetValue(ghost, out FrameworkElement? view) && view is TextBox box)
            {
                box.IsReadOnly = true;
                box.IsHitTestVisible = false;
                box.IsTabStop = false;
            }

            _ghostTexts[text.Id] = ghost;
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

    private void RemoveGhost(int key)
    {
        if (_ghostStrokes.Remove(key, out InkStroke? ghost))
        {
            foreach (InkStroke stroke in _document.Container.GetStrokes())
            {
                stroke.Selected = false;
            }

            ghost.Selected = true;
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
        foreach (int key in _ghostStrokes.Keys.ToList())
        {
            RemoveGhost(key);
        }

        foreach (Guid id in _ghostTexts.Keys.ToList())
        {
            RemoveTextGhost(id);
        }
    }
}
