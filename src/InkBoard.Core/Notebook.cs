using System.Text.Json;

namespace InkBoard.Core;

public sealed class NotebookPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled page";
    public List<NotebookPage> Children { get; set; } = [];
}

/// <summary>What is stored next to a page's ink: its objects and how far the page has grown.</summary>
public sealed class PageData
{
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// For each stroke in ink.isf, in file order: its key in the page's recording, or -1 if the recording
    /// does not know it.
    /// </summary>
    public List<int> StrokeKeys { get; set; } = [];

    public List<PageObject> Objects { get; set; } = [];
}

/// <summary>
/// The page tree: pages, subpages and sub-subpages, as in OneNote. Pure data and tree operations; where
/// the pages are stored is the app's business.
/// </summary>
public sealed class Notebook
{
    /// <summary>Page, subpage, sub-subpage.</summary>
    public const int MaxDepth = 3;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<NotebookPage> Pages { get; set; } = [];
    public Guid? LastOpenPageId { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static Notebook FromJson(string json) =>
        JsonSerializer.Deserialize<Notebook>(json, JsonOptions) ?? new Notebook();

    /// <summary>Every page, parents before their children, in display order.</summary>
    public IEnumerable<NotebookPage> AllPages() => Flatten(Pages);

    public NotebookPage? Find(Guid id) => AllPages().FirstOrDefault(p => p.Id == id);

    public NotebookPage? ParentOf(NotebookPage page) => AllPages().FirstOrDefault(p => p.Children.Contains(page));

    /// <summary>1 for a top-level page, 2 for a subpage, 3 for a sub-subpage.</summary>
    public int DepthOf(NotebookPage page)
    {
        int depth = 1;
        for (NotebookPage? parent = ParentOf(page); parent is not null; parent = ParentOf(parent))
        {
            depth++;
        }

        return depth;
    }

    /// <summary>Adds a page at the same level as <paramref name="after"/>, right below it and its subpages.</summary>
    public NotebookPage AddPage(string title, NotebookPage? after = null)
    {
        var page = new NotebookPage { Title = title };
        List<NotebookPage> siblings = after is null ? Pages : SiblingsOf(after);
        int index = after is null ? siblings.Count : siblings.IndexOf(after) + 1;
        siblings.Insert(index, page);
        return page;
    }

    public bool CanAddSubpage(NotebookPage parent) => DepthOf(parent) < MaxDepth;

    public NotebookPage AddSubpage(NotebookPage parent, string title)
    {
        if (!CanAddSubpage(parent))
        {
            throw new InvalidOperationException("Pages can only be nested three levels deep.");
        }

        var page = new NotebookPage { Title = title };
        parent.Children.Add(page);
        return page;
    }

    /// <summary>Removes a page with all its subpages and returns everything that was removed.</summary>
    public IReadOnlyList<NotebookPage> Remove(NotebookPage page)
    {
        List<NotebookPage> removed = Flatten([page]).ToList();
        SiblingsOf(page).Remove(page);
        if (LastOpenPageId is Guid last && removed.Any(p => p.Id == last))
        {
            LastOpenPageId = null;
        }

        return removed;
    }

    /// <summary>Makes the page a subpage of the page just above it at the same level.</summary>
    public bool CanDemote(NotebookPage page)
    {
        List<NotebookPage> siblings = SiblingsOf(page);
        return siblings.IndexOf(page) > 0 && DepthOf(page) + HeightOf(page) <= MaxDepth;
    }

    public bool Demote(NotebookPage page)
    {
        if (!CanDemote(page))
        {
            return false;
        }

        List<NotebookPage> siblings = SiblingsOf(page);
        NotebookPage newParent = siblings[siblings.IndexOf(page) - 1];
        siblings.Remove(page);
        newParent.Children.Add(page);
        return true;
    }

    /// <summary>Moves a subpage one level up, placing it right below its former parent.</summary>
    public bool Promote(NotebookPage page)
    {
        if (ParentOf(page) is not NotebookPage parent)
        {
            return false;
        }

        parent.Children.Remove(page);
        List<NotebookPage> siblings = SiblingsOf(parent);
        siblings.Insert(siblings.IndexOf(parent) + 1, page);
        return true;
    }

    /// <summary>Moves the page up (-1) or down (+1) among the pages at its level.</summary>
    public bool Move(NotebookPage page, int direction)
    {
        List<NotebookPage> siblings = SiblingsOf(page);
        int index = siblings.IndexOf(page), target = index + direction;
        if (target < 0 || target >= siblings.Count)
        {
            return false;
        }

        siblings.RemoveAt(index);
        siblings.Insert(target, page);
        return true;
    }

    private List<NotebookPage> SiblingsOf(NotebookPage page) => ParentOf(page)?.Children ?? Pages;

    /// <summary>Levels at and below the page: 1 if it has no subpages.</summary>
    private static int HeightOf(NotebookPage page) =>
        1 + (page.Children.Count == 0 ? 0 : page.Children.Max(HeightOf));

    private static IEnumerable<NotebookPage> Flatten(IEnumerable<NotebookPage> pages) =>
        pages.SelectMany(p => new[] { p }.Concat(Flatten(p.Children)));
}
