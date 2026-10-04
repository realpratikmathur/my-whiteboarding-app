using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using InkBoard.Core;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace InkBoard;

/// <summary>
/// One point-eraser gesture (pen down to pen up). A touched stroke is replaced by new strokes built by the
/// platform from runs of its own, unmodified ink points, with its drawing attributes and transform
/// carried over. Nothing is painted over and no point data is altered.
/// </summary>
public sealed class PartialEraseSession(InkDocument document)
{
    private readonly HashSet<int> _removedOriginals = [];
    private readonly HashSet<int> _createdFragments = [];

    public void EraseAlong(Point from, Point to, double eraserRadius)
    {
        foreach (InkStroke stroke in document.Container.GetStrokes().ToList())
        {
            Size tip = stroke.DrawingAttributes.Size;
            double radius = eraserRadius + Math.Max(tip.Width, tip.Height) / 2;
            if (!SweepTouches(stroke.BoundingRect, from, to, radius))
            {
                continue;
            }

            IReadOnlyList<InkPoint> inkPoints = stroke.GetInkPoints();
            Matrix3x2 transform = stroke.PointTransform;
            var positions = inkPoints
                .Select(p => Vector2.Transform(new Vector2((float)p.Position.X, (float)p.Position.Y), transform))
                .Select(v => new Point2(v.X, v.Y))
                .ToList();

            IReadOnlyList<PointRange> runs = StrokeSplitter.Split(
                positions, new Point2(from.X, from.Y), new Point2(to.X, to.Y), radius);
            if (StrokeSplitter.IsUntouched(runs, positions.Count))
            {
                continue;
            }

            var builder = new InkStrokeBuilder();
            builder.SetDefaultDrawingAttributes(stroke.DrawingAttributes);
            List<InkStroke> fragments = runs
                .Select(run => builder.CreateStrokeFromInkPoints(
                    inkPoints.Skip(run.Start).Take(run.Count), transform, stroke.StrokeStartedTime, stroke.StrokeDuration))
                .ToList();

            int handle = document.Track(stroke);
            document.Remove([handle]);
            if (_createdFragments.Remove(handle))
            {
                // A fragment made earlier in this gesture; undo never needs it.
                document.Forget(handle);
            }
            else
            {
                _removedOriginals.Add(handle);
            }

            foreach (InkStroke fragment in fragments)
            {
                _createdFragments.Add(document.AddNew(fragment));
            }
        }
    }

    /// <summary>The undo step for the whole gesture, or null if nothing was erased.</summary>
    public IUndoStep? Finish() => _removedOriginals.Count == 0
        ? null
        : new ReplaceStrokesStep(_removedOriginals.ToList(), _createdFragments.ToList());

    private static bool SweepTouches(Rect bounds, Point from, Point to, double radius)
    {
        double left = Math.Min(from.X, to.X) - radius, right = Math.Max(from.X, to.X) + radius;
        double top = Math.Min(from.Y, to.Y) - radius, bottom = Math.Max(from.Y, to.Y) + radius;
        return bounds.Right >= left && bounds.Left <= right && bounds.Bottom >= top && bounds.Top <= bottom;
    }
}
