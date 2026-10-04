using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class PageObjectTests
{
    private sealed class NoSurface : IStrokeSurface
    {
        public void Add(IReadOnlyList<int> strokeIds) => throw new InvalidOperationException();
        public void Remove(IReadOnlyList<int> strokeIds) => throw new InvalidOperationException();
        public void Move(IReadOnlyList<int> strokeIds, double dx, double dy) => throw new InvalidOperationException();
    }

    [Fact]
    public void Contains_covers_the_rectangle_including_its_edges()
    {
        var picture = new PageObject { Kind = PageObjectKind.Image, X = 10, Y = 20, Width = 100, Height = 50 };

        Assert.True(picture.Contains(10, 20));
        Assert.True(picture.Contains(110, 70));
        Assert.True(picture.Contains(60, 40));
        Assert.False(picture.Contains(9, 40));
        Assert.False(picture.Contains(60, 71));
    }

    [Fact]
    public void Action_step_runs_its_delegates_without_touching_strokes()
    {
        var undo = new UndoStack(new NoSurface());
        var text = new PageObject { Kind = PageObjectKind.Text, Text = "after" };
        undo.Push(new ActionStep(() => text.Text = "before", () => text.Text = "after"));

        undo.Undo();
        Assert.Equal("before", text.Text);

        undo.Redo();
        Assert.Equal("after", text.Text);
    }
}
