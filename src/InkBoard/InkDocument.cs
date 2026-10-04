using System.Collections.Generic;
using System.Linq;
using InkBoard.Core;
using Windows.Foundation;
using Windows.UI.Input.Inking;
using InkStrokeContainer = Microsoft.UI.Xaml.Controls.InkStrokeContainer;

namespace InkBoard;

/// <summary>
/// Bridges the platform stroke container to <see cref="IStrokeSurface"/>. Each stroke gets a stable app
/// handle, because a stroke that has been deleted can only come back as a <see cref="InkStroke.Clone"/>,
/// which is a new platform object with a new <see cref="InkStroke.Id"/>.
/// </summary>
public sealed class InkDocument(InkStrokeContainer container) : IStrokeSurface
{
    private readonly Dictionary<int, InkStroke> _strokes = [];       // handle -> latest platform object
    private readonly Dictionary<uint, int> _handleByStrokeId = [];   // only strokes currently in the container
    private int _nextHandle = 1;

    public InkStrokeContainer Container { get; } = container;

    /// <summary>Returns the handle of a stroke that is in the container, assigning one on first sight.</summary>
    public int Track(InkStroke stroke)
    {
        if (!_handleByStrokeId.TryGetValue(stroke.Id, out int handle))
        {
            handle = _nextHandle++;
            _strokes[handle] = stroke;
            _handleByStrokeId[stroke.Id] = handle;
        }

        return handle;
    }

    public IReadOnlyList<int> Track(IEnumerable<InkStroke> strokes) => strokes.Select(Track).ToList();

    /// <summary>Adds a stroke that has never been in the container.</summary>
    public int AddNew(InkStroke stroke)
    {
        Container.AddStroke(stroke);
        return Track(stroke);
    }

    /// <summary>
    /// Records strokes the platform has already removed (StrokesErased). Strokes that are not known to be
    /// in the container are skipped, so a removal is never recorded twice.
    /// </summary>
    public IReadOnlyList<int> MarkRemoved(IEnumerable<InkStroke> strokes)
    {
        var handles = new List<int>();
        foreach (InkStroke stroke in strokes)
        {
            if (_handleByStrokeId.Remove(stroke.Id, out int handle))
            {
                handles.Add(handle);
            }
        }

        return handles;
    }

    /// <summary>Empties the container and returns every stroke that was in it.</summary>
    public IReadOnlyList<int> ClearAll()
    {
        Track(Container.GetStrokes());
        List<int> handles = _handleByStrokeId.Values.ToList();
        Container.Clear();
        _handleByStrokeId.Clear();
        return handles;
    }

    public IReadOnlyList<int> SelectedHandles() => Track(Container.GetStrokes().Where(s => s.Selected));

    public InkStroke StrokeFor(int handle) => _strokes[handle];

    /// <summary>Drops a handle that no undo step will ever refer to.</summary>
    public void Forget(int handle) => _strokes.Remove(handle);

    public void SelectOnly(IReadOnlyCollection<int> handles)
    {
        foreach (InkStroke stroke in Container.GetStrokes())
        {
            stroke.Selected = _handleByStrokeId.TryGetValue(stroke.Id, out int handle) && handles.Contains(handle);
        }
    }

    public void ClearSelection() => SelectOnly([]);

    // ---- IStrokeSurface -------------------------------------------------------------------------

    public void Add(IReadOnlyList<int> strokeIds)
    {
        foreach (int handle in strokeIds)
        {
            InkStroke clone = _strokes[handle].Clone();
            Container.AddStroke(clone);
            _strokes[handle] = clone;
            _handleByStrokeId[clone.Id] = handle;
        }
    }

    public void Remove(IReadOnlyList<int> strokeIds)
    {
        SelectOnly(strokeIds);
        Container.DeleteSelected();
        foreach (int handle in strokeIds)
        {
            _handleByStrokeId.Remove(_strokes[handle].Id);
        }
    }

    public void Move(IReadOnlyList<int> strokeIds, double dx, double dy)
    {
        SelectOnly(strokeIds);
        Container.MoveSelected(new Point(dx, dy));
    }
}
