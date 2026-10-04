using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class NotebookTests
{
    private static string Outline(Notebook notebook) =>
        string.Join(" ", notebook.AllPages().Select(p => new string('>', notebook.DepthOf(p) - 1) + p.Title));

    [Fact]
    public void Pages_are_added_at_the_end_or_after_a_given_page()
    {
        var notebook = new Notebook();
        NotebookPage a = notebook.AddPage("A");
        notebook.AddPage("C");
        notebook.AddPage("B", after: a);

        Assert.Equal("A B C", Outline(notebook));
    }

    [Fact]
    public void New_page_after_a_page_with_subpages_goes_below_them()
    {
        var notebook = new Notebook();
        NotebookPage a = notebook.AddPage("A");
        notebook.AddSubpage(a, "A1");
        notebook.AddPage("B", after: a);

        Assert.Equal("A >A1 B", Outline(notebook));
    }

    [Fact]
    public void Subpages_nest_three_levels_and_no_deeper()
    {
        var notebook = new Notebook();
        NotebookPage page = notebook.AddPage("Page");
        NotebookPage sub = notebook.AddSubpage(page, "Sub");
        NotebookPage subSub = notebook.AddSubpage(sub, "SubSub");

        Assert.Equal("Page >Sub >>SubSub", Outline(notebook));
        Assert.False(notebook.CanAddSubpage(subSub));
        Assert.Throws<InvalidOperationException>(() => notebook.AddSubpage(subSub, "Too deep"));
    }

    [Fact]
    public void Removing_a_page_removes_its_subpages_and_forgets_the_last_open_page()
    {
        var notebook = new Notebook();
        NotebookPage a = notebook.AddPage("A");
        NotebookPage a1 = notebook.AddSubpage(a, "A1");
        notebook.AddPage("B");
        notebook.LastOpenPageId = a1.Id;

        IReadOnlyList<NotebookPage> removed = notebook.Remove(a);

        Assert.Equal([a, a1], removed);
        Assert.Equal("B", Outline(notebook));
        Assert.Null(notebook.LastOpenPageId);
    }

    [Fact]
    public void Demote_makes_a_page_a_subpage_of_the_one_above()
    {
        var notebook = new Notebook();
        notebook.AddPage("A");
        NotebookPage b = notebook.AddPage("B");

        Assert.True(notebook.Demote(b));
        Assert.Equal("A >B", Outline(notebook));
    }

    [Fact]
    public void First_page_at_a_level_cannot_be_demoted()
    {
        var notebook = new Notebook();
        NotebookPage a = notebook.AddPage("A");

        Assert.False(notebook.Demote(a));
    }

    [Fact]
    public void Demote_is_refused_when_subpages_would_end_up_too_deep()
    {
        var notebook = new Notebook();
        NotebookPage a = notebook.AddPage("A");
        NotebookPage a1 = notebook.AddSubpage(a, "A1");
        NotebookPage a2 = notebook.AddSubpage(a, "A2");
        notebook.AddSubpage(a2, "A2x");

        // A2 has a subpage; under A1 that subpage would be level 4.
        Assert.False(notebook.Demote(a2));
        Assert.Equal("A >A1 >A2 >>A2x", Outline(notebook));
        Assert.NotNull(a1);
    }

    [Fact]
    public void Promote_places_a_subpage_right_below_its_former_parent()
    {
        var notebook = new Notebook();
        NotebookPage a = notebook.AddPage("A");
        NotebookPage a1 = notebook.AddSubpage(a, "A1");
        notebook.AddPage("B");

        Assert.True(notebook.Promote(a1));
        Assert.Equal("A A1 B", Outline(notebook));
        Assert.False(notebook.Promote(a1));
    }

    [Fact]
    public void Move_reorders_within_the_same_level_only()
    {
        var notebook = new Notebook();
        NotebookPage a = notebook.AddPage("A");
        notebook.AddSubpage(a, "A1");
        NotebookPage b = notebook.AddPage("B");

        Assert.True(notebook.Move(b, -1));
        Assert.Equal("B A >A1", Outline(notebook));
        Assert.False(notebook.Move(b, -1));
    }

    [Fact]
    public void Json_round_trip_keeps_tree_ids_and_last_open_page()
    {
        var notebook = new Notebook();
        NotebookPage a = notebook.AddPage("Algebra");
        NotebookPage a1 = notebook.AddSubpage(a, "Quadratics");
        notebook.AddPage("Geometry");
        notebook.LastOpenPageId = a1.Id;

        Notebook loaded = Notebook.FromJson(notebook.ToJson());

        Assert.Equal("Algebra >Quadratics Geometry", Outline(loaded));
        Assert.Equal(a1.Id, loaded.LastOpenPageId);
        Assert.Equal(a1.Id, loaded.Find(a1.Id)!.Id);
    }

    [Fact]
    public void Page_data_round_trips_objects()
    {
        var data = new PageData { Width = 4000, Height = 12000 };
        data.Objects.Add(new PageObject { Kind = PageObjectKind.Text, Text = "Hello\rworld", X = 5, Y = 6, Color = "112233" });
        data.Objects.Add(new PageObject { Kind = PageObjectKind.Image, Asset = "a.png", Width = 100, Height = 50 });

        string json = System.Text.Json.JsonSerializer.Serialize(data);
        PageData loaded = System.Text.Json.JsonSerializer.Deserialize<PageData>(json)!;

        Assert.Equal(12000, loaded.Height);
        Assert.Equal("Hello\rworld", loaded.Objects[0].Text);
        Assert.Equal("112233", loaded.Objects[0].Color);
        Assert.Equal(PageObjectKind.Image, loaded.Objects[1].Kind);
        Assert.Equal("a.png", loaded.Objects[1].Asset);
        Assert.Equal(data.Objects[1].Id, loaded.Objects[1].Id);
    }
}
