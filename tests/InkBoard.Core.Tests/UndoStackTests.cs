using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class UndoStackTests
{
    private sealed class FakeSurface : IStrokeSurface
    {
        public HashSet<int> Live { get; } = [];
        public Dictionary<int, (double X, double Y)> Offsets { get; } = [];

        public void Add(IReadOnlyList<int> strokeIds) => Live.UnionWith(strokeIds);

        public void Remove(IReadOnlyList<int> strokeIds) => Live.ExceptWith(strokeIds);

        public void Move(IReadOnlyList<int> strokeIds, double dx, double dy)
        {
            foreach (int id in strokeIds)
            {
                var (x, y) = Offsets.GetValueOrDefault(id);
                Offsets[id] = (x + dx, y + dy);
            }
        }
    }

    [Fact]
    public void Add_can_be_undone_and_redone()
    {
        var surface = new FakeSurface();
        var undo = new UndoStack(surface);
        surface.Add([1]);
        undo.Push(new AddStrokesStep([1]));

        Assert.True(undo.Undo());
        Assert.Empty(surface.Live);

        Assert.True(undo.Redo());
        Assert.Equal([1], surface.Live);
    }

    [Fact]
    public void Erase_can_be_undone_and_redone()
    {
        var surface = new FakeSurface();
        var undo = new UndoStack(surface);
        surface.Add([1, 2]);
        surface.Remove([2]);
        undo.Push(new RemoveStrokesStep([2]));

        undo.Undo();
        Assert.Equal([1, 2], surface.Live.Order());

        undo.Redo();
        Assert.Equal([1], surface.Live);
    }

    [Fact]
    public void Move_is_reversed_by_the_opposite_translation()
    {
        var surface = new FakeSurface();
        var undo = new UndoStack(surface);
        surface.Move([1], 30, -5);
        undo.Push(new MoveStrokesStep([1], 30, -5));

        undo.Undo();
        Assert.Equal((0, 0), surface.Offsets[1]);

        undo.Redo();
        Assert.Equal((30, -5), surface.Offsets[1]);
    }

    [Fact]
    public void Partial_erase_swaps_fragments_and_original()
    {
        var surface = new FakeSurface();
        var undo = new UndoStack(surface);
        surface.Add([1]);
        surface.Remove([1]);
        surface.Add([2, 3]);
        undo.Push(new ReplaceStrokesStep([1], [2, 3]));

        undo.Undo();
        Assert.Equal([1], surface.Live);

        undo.Redo();
        Assert.Equal([2, 3], surface.Live.Order());
    }

    [Fact]
    public void Steps_undo_in_reverse_order()
    {
        var surface = new FakeSurface();
        var undo = new UndoStack(surface);
        surface.Add([1]);
        undo.Push(new AddStrokesStep([1]));
        surface.Add([2]);
        undo.Push(new AddStrokesStep([2]));

        undo.Undo();
        Assert.Equal([1], surface.Live);
        undo.Undo();
        Assert.Empty(surface.Live);
        Assert.False(undo.Undo());
    }

    [Fact]
    public void New_change_discards_redo_history()
    {
        var surface = new FakeSurface();
        var undo = new UndoStack(surface);
        surface.Add([1]);
        undo.Push(new AddStrokesStep([1]));
        undo.Undo();
        Assert.True(undo.CanRedo);

        surface.Add([2]);
        undo.Push(new AddStrokesStep([2]));

        Assert.False(undo.CanRedo);
        Assert.False(undo.Redo());
        Assert.Equal([2], surface.Live);
    }

    [Fact]
    public void Changed_fires_on_push_undo_and_redo()
    {
        var undo = new UndoStack(new FakeSurface());
        int changes = 0;
        undo.Changed += (_, _) => changes++;

        undo.Push(new AddStrokesStep([1]));
        undo.Undo();
        undo.Redo();

        Assert.Equal(3, changes);
        Assert.True(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }
}
