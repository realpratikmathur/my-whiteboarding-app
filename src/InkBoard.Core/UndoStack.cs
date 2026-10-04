namespace InkBoard.Core;

/// <summary>
/// What undo/redo needs from the ink surface. Strokes are referred to by app-assigned ids because the
/// platform stroke object changes identity every time a stroke is re-added to a container.
/// </summary>
public interface IStrokeSurface
{
    void Add(IReadOnlyList<int> strokeIds);
    void Remove(IReadOnlyList<int> strokeIds);
    void Move(IReadOnlyList<int> strokeIds, double dx, double dy);
}

/// <summary>A change that has already happened on the surface and can be reversed and replayed.</summary>
public interface IUndoStep
{
    void Undo(IStrokeSurface surface);
    void Redo(IStrokeSurface surface);
}

public sealed record AddStrokesStep(IReadOnlyList<int> StrokeIds) : IUndoStep
{
    public void Undo(IStrokeSurface surface) => surface.Remove(StrokeIds);
    public void Redo(IStrokeSurface surface) => surface.Add(StrokeIds);
}

public sealed record RemoveStrokesStep(IReadOnlyList<int> StrokeIds) : IUndoStep
{
    public void Undo(IStrokeSurface surface) => surface.Add(StrokeIds);
    public void Redo(IStrokeSurface surface) => surface.Remove(StrokeIds);
}

public sealed record MoveStrokesStep(IReadOnlyList<int> StrokeIds, double Dx, double Dy) : IUndoStep
{
    public void Undo(IStrokeSurface surface) => surface.Move(StrokeIds, -Dx, -Dy);
    public void Redo(IStrokeSurface surface) => surface.Move(StrokeIds, Dx, Dy);
}

/// <summary>Strokes swapped for other strokes: partial erase (originals for fragments), ink-to-shape.</summary>
public sealed record ReplaceStrokesStep(IReadOnlyList<int> RemovedIds, IReadOnlyList<int> AddedIds) : IUndoStep
{
    public void Undo(IStrokeSurface surface)
    {
        surface.Remove(AddedIds);
        surface.Add(RemovedIds);
    }

    public void Redo(IStrokeSurface surface)
    {
        surface.Remove(RemovedIds);
        surface.Add(AddedIds);
    }
}

/// <summary>
/// A stroke change that comes with something outside the stroke surface (for example the text box that
/// replaces ink in "Ink to text").
/// </summary>
public sealed record CompanionStep(IUndoStep Inner, Action AfterUndo, Action AfterRedo) : IUndoStep
{
    public void Undo(IStrokeSurface surface)
    {
        Inner.Undo(surface);
        AfterUndo();
    }

    public void Redo(IStrokeSurface surface)
    {
        Inner.Redo(surface);
        AfterRedo();
    }
}

public sealed class UndoStack(IStrokeSurface surface)
{
    private readonly Stack<IUndoStep> _undo = new();
    private readonly Stack<IUndoStep> _redo = new();

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records a change that was just made. Discards anything that could have been redone.</summary>
    public void Push(IUndoStep step)
    {
        _undo.Push(step);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out IUndoStep? step))
        {
            return false;
        }

        step.Undo(surface);
        _redo.Push(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out IUndoStep? step))
        {
            return false;
        }

        step.Redo(surface);
        _undo.Push(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
