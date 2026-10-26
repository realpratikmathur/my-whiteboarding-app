using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class GhostReplayTests
{
    private static RecordedEvent Add(params int[] keys) => new() { Kind = RecordedEventKind.StrokeAdd, Strokes = [.. keys] };

    private static RecordedEvent Move(double dx, double dy, params int[] keys) =>
        new() { Kind = RecordedEventKind.StrokeMove, Strokes = [.. keys], Dx = dx, Dy = dy };

    [Fact]
    public void Final_page_knows_where_moved_strokes_ended_up()
    {
        var recording = new Recording { Events = [Add(0, 1), Move(10, 5, 0), Move(-3, 20, 0, 1)] };

        RecordingState end = RecordingState.AtEnd(recording);

        Assert.Equal((7, 25), end.Offsets[0]);
        Assert.Equal((-3, 20), end.Offsets[1]);
    }

    [Fact]
    public void Strokes_that_were_never_moved_have_no_offset()
    {
        RecordingState end = RecordingState.AtEnd(new Recording { Events = [Add(0)] });

        Assert.False(end.Offsets.ContainsKey(0));
    }

    [Fact]
    public void Erased_strokes_are_not_part_of_the_final_page()
    {
        var recording = new Recording
        {
            Events = [Add(0, 1), new RecordedEvent { Kind = RecordedEventKind.StrokeRemove, Strokes = [0] }],
        };

        Assert.Equal([1], RecordingState.AtEnd(recording).Strokes);
    }

    [Fact]
    public void Blank_stays_the_default_so_existing_notebooks_replay_as_before()
    {
        Assert.Equal(ReplayStartMode.Blank, new Notebook().ReplayStart);
        Assert.Equal(ReplayStartMode.Blank, Notebook.FromJson("""{"Pages":[]}""").ReplayStart);
    }

    [Fact]
    public void Start_mode_is_remembered_in_the_notebook()
    {
        var notebook = new Notebook { ReplayStart = ReplayStartMode.Ghost };

        Assert.Equal(ReplayStartMode.Ghost, Notebook.FromJson(notebook.ToJson()).ReplayStart);
    }
}
