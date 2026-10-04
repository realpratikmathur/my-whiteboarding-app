using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class PageExtentTests
{
    [Fact]
    public void Stays_put_while_there_is_room()
    {
        Assert.Equal(3000, PageExtent.Grow(3000, contentEdge: 500, viewEdge: 900, margin: 900, PageExtent.MinHeight));
    }

    [Fact]
    public void Grows_in_whole_chunks_when_the_view_nears_the_edge()
    {
        // View bottom at 2600 + one viewport (900) = 3500 needed -> next chunk boundary.
        Assert.Equal(4000, PageExtent.Grow(3000, contentEdge: 500, viewEdge: 2600, margin: 900, PageExtent.MinHeight));
    }

    [Fact]
    public void Grows_for_content_far_below_the_view()
    {
        Assert.Equal(60000, PageExtent.Grow(3000, contentEdge: 58200, viewEdge: 900, margin: 900, PageExtent.MinHeight));
    }

    [Fact]
    public void Never_shrinks_and_never_goes_below_the_minimum()
    {
        Assert.Equal(10000, PageExtent.Grow(10000, contentEdge: 0, viewEdge: 0, margin: 900, PageExtent.MinHeight));
        Assert.Equal(PageExtent.MinWidth, PageExtent.Grow(0, contentEdge: 0, viewEdge: 0, margin: 1400, PageExtent.MinWidth));
    }

    [Fact]
    public void Keeps_growing_without_a_limit()
    {
        double height = PageExtent.MinHeight;
        for (double viewBottom = 1000; viewBottom <= 500_000; viewBottom += 700)
        {
            height = PageExtent.Grow(height, 0, viewBottom, 900, PageExtent.MinHeight);
            Assert.True(height >= viewBottom + 900);
        }

        Assert.True(height >= 500_000);
    }
}
