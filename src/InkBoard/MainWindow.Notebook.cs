using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkBoard.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Streams;
using OsInkStrokeContainer = Windows.UI.Input.Inking.InkStrokeContainer;

namespace InkBoard;

// Notebook: pages and subpages stored under Documents\InkBoard, saved automatically and reopened on start.
//
//   notebook.json                 page tree + last open page
//   pages\<page id>\ink.isf       the page's strokes
//   pages\<page id>\page.json     text boxes, pictures, page size
//   pages\<page id>\assets\*.png  picture files
public sealed partial class MainWindow
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromSeconds(2);

    private readonly Dictionary<TreeViewNode, NotebookPage> _pageNodes = [];
    private readonly SemaphoreSlim _pageIo = new(1, 1);
    private readonly TaskCompletionSource _notebookReady = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = AutoSaveDelay };

    private string _notebookFolder = "";
    private Notebook _notebook = new();
    private NotebookPage? _currentPage;
    private bool _loadingPage;
    private bool _pageDirty;
    private bool _closing;

    /// <summary>Completes when the notebook has been opened and its last page is on screen.</summary>
    internal Task NotebookReady => _notebookReady.Task;

    private string NotebookIndexPath => Path.Combine(_notebookFolder, "notebook.json");

    private string PageFolder(NotebookPage page) => Path.Combine(_notebookFolder, "pages", page.Id.ToString("N"));

    private void ConfigureNotebook()
    {
        _notebookFolder = SelfTest.NotebookFolderOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "InkBoard");

        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            await SaveCurrentPageAsync();
        };
        Root.Loaded += async (_, _) =>
        {
            try
            {
                await OpenNotebookAsync();
                _notebookReady.TrySetResult();
            }
            catch (Exception e)
            {
                StatusText.Text = "Could not open the notebook: " + e.Message;
                _notebookReady.TrySetException(e);
            }
        };
        Closed += OnWindowClosed;
    }

    // ---- Open / save ----------------------------------------------------------------------------

    private async Task OpenNotebookAsync()
    {
        Directory.CreateDirectory(_notebookFolder);
        _notebook = File.Exists(NotebookIndexPath)
            ? Notebook.FromJson(await File.ReadAllTextAsync(NotebookIndexPath))
            : new Notebook();
        if (_notebook.Pages.Count == 0)
        {
            _notebook.AddPage("Page 1");
        }

        NotebookPage start = (_notebook.LastOpenPageId is Guid id ? _notebook.Find(id) : null) ?? _notebook.Pages[0];
        RebuildPageTree();
        FollowBox.SelectedIndex = _notebook.ReplayFollow switch
        {
            ReplayFollowMode.EdgeNudge => 1,
            ReplayFollowMode.Off => 2,
            _ => 0,
        };
        StartBox.SelectedIndex = _notebook.ReplayStart == ReplayStartMode.Ghost ? 1 : 0;
        await OpenPageAsync(start);
    }

    private void SaveNotebookIndex() => File.WriteAllText(NotebookIndexPath, _notebook.ToJson());

    /// <summary>Called for every change to the open page; saves a moment after the last one.</summary>
    private void MarkPageDirty()
    {
        if (_loadingPage || _replaying || _currentPage is null)
        {
            return;
        }

        _pageDirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    internal async Task SaveCurrentPageAsync()
    {
        if (!_pageDirty || _currentPage is not NotebookPage page)
        {
            return;
        }

        _pageDirty = false;
        await _pageIo.WaitAsync();
        try
        {
            string folder = PageFolder(page);
            Directory.CreateDirectory(folder);

            string inkPath = Path.Combine(folder, "ink.isf");
            if (_document.Container.GetStrokes().Count > 0)
            {
                await SaveIsfAsync(inkPath);
            }
            else
            {
                File.Delete(inkPath);
            }

            var data = new PageData
            {
                Width = PageSurface.Width,
                Height = PageSurface.Height,
                // A text box that is still empty (just placed, nothing typed) is not worth keeping.
                Objects = [.. _objects.Where(o => o.Kind != PageObjectKind.Text || !string.IsNullOrWhiteSpace(o.Text))],
                // Same order as the strokes in ink.isf.
                StrokeKeys = _document.Track(_document.Container.GetStrokes())
                    .Select(handle => _strokeKeys.GetValueOrDefault(handle, -1)).ToList(),
            };
            await File.WriteAllTextAsync(Path.Combine(folder, "page.json"), JsonSerializer.Serialize(data));
            await SaveRecordingAsync(folder);
        }
        catch (Exception e)
        {
            _pageDirty = true;
            StatusText.Text = "Auto-save failed: " + e.Message;
        }
        finally
        {
            _pageIo.Release();
        }
    }

    /// <summary>Hook for the recorder's per-page files.</summary>
    private partial Task SaveRecordingAsync(string pageFolder);

    private partial Task LoadRecordingAsync(string pageFolder);

    internal async Task OpenPageAsync(NotebookPage page)
    {
        if (ReferenceEquals(page, _currentPage))
        {
            return;
        }

        StopRecording();
        _saveTimer.Stop();
        Root.Focus(FocusState.Programmatic);   // commits a text box that is being edited
        await SaveCurrentPageAsync();

        await _pageIo.WaitAsync();
        _loadingPage = true;
        try
        {
            ClearSelection();
            SelectObject(null);
            _document.ClearAll();
            ClearObjects();
            _strokeKeys.Clear();
            List<int> strokeKeys = [];

            _currentPage = page;
            string folder = PageFolder(page);
            _assetFolder = Path.Combine(folder, "assets");
            PageSurface.Width = PageExtent.MinWidth;
            PageSurface.Height = PageExtent.MinHeight;

            string dataPath = Path.Combine(folder, "page.json");
            if (File.Exists(dataPath))
            {
                PageData data = JsonSerializer.Deserialize<PageData>(await File.ReadAllTextAsync(dataPath)) ?? new PageData();
                PageSurface.Width = Math.Max(data.Width, PageExtent.MinWidth);
                PageSurface.Height = Math.Max(data.Height, PageExtent.MinHeight);
                strokeKeys = data.StrokeKeys;
                foreach (PageObject obj in data.Objects)
                {
                    ShowObject(obj);
                }
            }

            string inkPath = Path.Combine(folder, "ink.isf");
            if (File.Exists(inkPath))
            {
                var loaded = new OsInkStrokeContainer();
                using (IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(inkPath, FileAccessMode.Read))
                using (IInputStream input = stream.GetInputStreamAt(0))
                {
                    await loaded.LoadAsync(input);
                }

                int index = 0;
                foreach (var stroke in loaded.GetStrokes())
                {
                    int handle = _document.AddNew(stroke.Clone());
                    if (index < strokeKeys.Count && strokeKeys[index] >= 0)
                    {
                        _strokeKeys[handle] = strokeKeys[index];
                    }

                    index++;
                }
            }

            await LoadRecordingAsync(folder);
            _undo.Clear();
            CanvasScroller.ChangeView(0, 0, 1f, disableAnimation: true);
        }
        finally
        {
            _loadingPage = false;
            _pageIo.Release();
        }

        OnPageContentChanged();
        _pageDirty = false;
        _saveTimer.Stop();

        _notebook.LastOpenPageId = page.Id;
        SaveNotebookIndex();
        SelectPageNode(page);
        Title = "InkBoard - " + page.Title;
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        // Finish saving before the window goes away.
        args.Handled = true;
        _closing = true;
        _saveTimer.Stop();
        Root.Focus(FocusState.Programmatic);
        try
        {
            StopRecording();
            await SaveCurrentPageAsync();
            SaveNotebookIndex();
        }
        finally
        {
            // When nothing needed saving, the awaits above complete synchronously and this is still inside
            // the Closed event that was just marked handled; a Close() made here would be swallowed with
            // it. Close from a fresh dispatcher turn instead (_closing lets that one through).
            DispatcherQueue.TryEnqueue(Close);
        }
    }

    // ---- Page tree ------------------------------------------------------------------------------

    private void RebuildPageTree()
    {
        _pageNodes.Clear();
        PageTree.RootNodes.Clear();
        foreach (NotebookPage page in _notebook.Pages)
        {
            PageTree.RootNodes.Add(CreatePageNode(page));
        }

        if (_currentPage is not null)
        {
            SelectPageNode(_currentPage);
        }
    }

    private TreeViewNode CreatePageNode(NotebookPage page)
    {
        var node = new TreeViewNode { Content = page.Title, IsExpanded = true };
        _pageNodes[node] = page;
        foreach (NotebookPage child in page.Children)
        {
            node.Children.Add(CreatePageNode(child));
        }

        return node;
    }

    private void SelectPageNode(NotebookPage page)
    {
        TreeViewNode? node = _pageNodes.FirstOrDefault(pair => ReferenceEquals(pair.Value, page)).Key;
        if (node is not null)
        {
            PageTree.SelectedNode = node;
        }
    }

    private async void OnPageInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && _pageNodes.TryGetValue(node, out NotebookPage? page))
        {
            await OpenPageAsync(page);
        }
    }

    private async Task AfterTreeChangedAsync(NotebookPage open)
    {
        SaveNotebookIndex();
        RebuildPageTree();
        await OpenPageAsync(open);
        SelectPageNode(open);
    }

    internal async Task<NotebookPage> AddPageAsync(string? title = null)
    {
        NotebookPage page = _notebook.AddPage(title ?? $"Page {_notebook.AllPages().Count() + 1}");
        await AfterTreeChangedAsync(page);
        return page;
    }

    /// <summary>Adds a subpage under <paramref name="parent"/> (the open page if not given) and opens it.</summary>
    internal async Task<NotebookPage?> AddSubpageAsync(string? title = null, NotebookPage? parent = null)
    {
        parent ??= _currentPage;
        if (parent is null || !_notebook.CanAddSubpage(parent))
        {
            StatusText.Text = "Subpages can be nested three levels deep";
            return null;
        }

        NotebookPage page = _notebook.AddSubpage(parent, title ?? $"Page {_notebook.AllPages().Count() + 1}");
        await AfterTreeChangedAsync(page);
        return page;
    }

    // ---- Dialogs --------------------------------------------------------------------------------

    /// <summary>
    /// Shows a dialog with ink input switched off. The InkCanvas takes pen and mouse input on its own
    /// thread regardless of what XAML draws over it, so without this a click on a dialog button would be
    /// inked onto the page underneath instead of reaching the button.
    /// </summary>
    internal async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = Root.XamlRoot;
        Presenter.IsInputEnabled = false;
        if (_middleButton is not null)
        {
            _middleButton.Enabled = false;
        }

        Presenter.InputDeviceTypes = Windows.UI.Core.CoreInputDeviceTypes.None;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            ApplyInputDevices();
            Presenter.IsInputEnabled = !_replaying;   // replay keeps input off until it is left
            if (_middleButton is not null)
            {
                _middleButton.Enabled = true;
            }
        }
    }

    // ---- Page commands (the "…" button acts on the open page, the right-click menu on the clicked one) --

    private async void OnAddPageClick(object sender, RoutedEventArgs e) => await AddPageAsync();

    private async void OnAddSubpageClick(object sender, RoutedEventArgs e) => await AddSubpageAsync();

    private async void OnRenamePageClick(object sender, RoutedEventArgs e) => await RenamePageAsync(_currentPage);

    private void OnMovePageUpClick(object sender, RoutedEventArgs e) => ReorderPage(_currentPage, p => _notebook.Move(p, -1));

    private void OnMovePageDownClick(object sender, RoutedEventArgs e) => ReorderPage(_currentPage, p => _notebook.Move(p, 1));

    private void OnDemotePageClick(object sender, RoutedEventArgs e) => ReorderPage(_currentPage, _notebook.Demote);

    private void OnPromotePageClick(object sender, RoutedEventArgs e) => ReorderPage(_currentPage, _notebook.Promote);

    private async void OnDeletePageClick(object sender, RoutedEventArgs e) => await ConfirmDeletePageAsync(_currentPage);

    /// <summary>Right-click, pen press-and-hold or the menu key on the page list.</summary>
    private void OnPageTreeContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (_replaying)
        {
            return;
        }

        NotebookPage? page = PageFromElement(args.OriginalSource as DependencyObject);
        MenuFlyout menu = BuildPageMenu(page);
        if (args.TryGetPosition(PageTree, out Windows.Foundation.Point position))
        {
            menu.ShowAt(PageTree, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = position });
        }
        else
        {
            menu.ShowAt(PageTree);
        }

        args.Handled = true;
    }

    /// <summary>The page whose row contains the element, or null for empty space in the list.</summary>
    private NotebookPage? PageFromElement(DependencyObject? element)
    {
        for (; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is TreeViewItem item
                && PageTree.NodeFromContainer(item) is TreeViewNode node
                && _pageNodes.TryGetValue(node, out NotebookPage? page))
            {
                return page;
            }
        }

        return null;
    }

    /// <summary>The page commands for one page; items that cannot apply to it are disabled.</summary>
    internal MenuFlyout BuildPageMenu(NotebookPage? page)
    {
        var menu = new MenuFlyout();
        void Add(string text, Action action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (page is null)
        {
            Add("New page", async () => await AddPageAsync());
            return menu;
        }

        var siblings = _notebook.ParentOf(page)?.Children ?? _notebook.Pages;
        int index = siblings.IndexOf(page);
        Add("New subpage", async () => await AddSubpageAsync(parent: page), _notebook.CanAddSubpage(page));
        Add("Rename…", async () => await RenamePageAsync(page));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Move up", () => ReorderPage(page, p => _notebook.Move(p, -1)), index > 0);
        Add("Move down", () => ReorderPage(page, p => _notebook.Move(p, 1)), index < siblings.Count - 1);
        Add("Make subpage", () => ReorderPage(page, _notebook.Demote), _notebook.CanDemote(page));
        Add("Promote", () => ReorderPage(page, _notebook.Promote), _notebook.ParentOf(page) is not null);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Delete…", async () => await ConfirmDeletePageAsync(page));
        return menu;
    }

    internal async Task RenamePageAsync(NotebookPage? page, string? newTitle = null)
    {
        if (page is null)
        {
            return;
        }

        if (newTitle is not null)
        {
            ApplyTitle(page, newTitle);
            return;
        }

        var box = new TextBox { Text = page.Title, SelectionStart = 0, SelectionLength = page.Title.Length };
        var dialog = new ContentDialog
        {
            Title = "Rename page",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            ApplyTitle(page, box.Text);
        }
    }

    private void ApplyTitle(NotebookPage page, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        page.Title = title.Trim();
        SaveNotebookIndex();
        RebuildPageTree();
        if (ReferenceEquals(page, _currentPage))
        {
            Title = "InkBoard - " + page.Title;
        }
    }

    internal void ReorderPage(NotebookPage? page, Func<NotebookPage, bool> change)
    {
        if (page is not null && change(page))
        {
            SaveNotebookIndex();
            RebuildPageTree();
        }
    }

    private async Task ConfirmDeletePageAsync(NotebookPage? page)
    {
        if (page is null)
        {
            return;
        }

        int subpages = page.Children.Count;
        var dialog = new ContentDialog
        {
            Title = $"Delete \"{page.Title}\"?",
            Content = (subpages > 0 ? "This page and all its subpages" : "This page") +
                      " will be deleted from your notebook, including ink, pictures and recording. This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            await DeletePageAsync(page);
        }
    }

    internal async Task DeletePageAsync(NotebookPage page)
    {
        _saveTimer.Stop();
        IReadOnlyList<NotebookPage> removed = _notebook.Remove(page);
        if (removed.Contains(_currentPage))
        {
            // The open page is going away: nothing of it needs saving.
            StopRecording();
            _pageDirty = false;
            _currentPage = null;
        }
        else
        {
            // Another page is being deleted: keep any unsaved work on the open one.
            await SaveCurrentPageAsync();
        }

        await _pageIo.WaitAsync();
        try
        {
            foreach (NotebookPage gone in removed)
            {
                if (Directory.Exists(PageFolder(gone)))
                {
                    Directory.Delete(PageFolder(gone), recursive: true);
                }
            }
        }
        finally
        {
            _pageIo.Release();
        }

        if (_notebook.Pages.Count == 0)
        {
            _notebook.AddPage("Page 1");
        }

        await AfterTreeChangedAsync(_currentPage ?? _notebook.Pages[0]);
    }
}
