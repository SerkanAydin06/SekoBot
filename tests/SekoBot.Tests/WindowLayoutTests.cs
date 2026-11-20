using System.Drawing;
using SekoBot.UI;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Several games on one screen: where the game windows and the bots' bars go.</summary>
public class WindowLayoutTests
{
    private static readonly Rectangle Work = new(0, 0, 1920, 1032);
    private static readonly Size Frame = new(2, 32);   // title bar and borders
    private const int Band = 86;                        // the bot's bar under each game

    [Fact]
    public void Six_games_are_laid_out_480_high_so_their_screens_show_whole()
    {
        // The game cuts its screens off below 480 (seen: the bag's bottom out of a 358-high window): 6 games on
        // 1920×1032 go 640×480, side by side without gaps, a 36-pixel bar under each — the whole height used.
        var cells = WindowLayout.UiWholeGrid(Work, 6, 36)!;
        Assert.Equal(6, cells.Count);
        Assert.All(cells, c => Assert.Equal(new Size(640, 480), c.Size));
        Assert.Equal(new Rectangle(0, 0, 640, 480), cells[0]);
        Assert.Equal(new Rectangle(1280, 516, 640, 480), cells[5]);
        Assert.All(cells, c => Assert.True(Work.Contains(new Rectangle(c.X, c.Y, c.Width, c.Height + 36))));
        // Fewer games: wider pictures, still 480 high, centred.
        var two = WindowLayout.UiWholeGrid(Work, 2, 36)!;
        Assert.All(two, c => Assert.Equal(480, c.Height));
        // Too many to fit 480 high: none (the usual grid is kept).
        Assert.Null(WindowLayout.UiWholeGrid(Work, 9, 36));
    }

    [Fact]
    public void A_small_game_grows_in_its_place_just_enough_for_whole_screens()
    {
        // A borderless 636×358 game at the right edge: 853×480 in its shape, kept on the work area.
        var grown = WindowLayout.UiWhole(new Rectangle(1284, 519, 636, 358), new Size(636, 358), Work);
        Assert.Equal(new Rectangle(1920 - 853, 519, 853, 480), grown);
        // With a title bar: the picture is what gets 480 high.
        grown = WindowLayout.UiWhole(new Rectangle(10, 20, 638, 390), new Size(636, 358), Work);
        Assert.Equal(new Rectangle(10, 20, 855, 512), grown);
        // Big enough already: left as it is.
        var big = new Rectangle(0, 0, 1920, 1032);
        Assert.Equal(big, WindowLayout.UiWhole(big, new Size(1920, 1032), Work));
    }

    private static void Tidy(List<Rectangle> cells, int games)
    {
        Assert.Equal(games, cells.Count);
        Assert.All(cells, c => Assert.True(Work.Contains(new Rectangle(c.X, c.Y, c.Width, c.Height + Band))));
        // Never squeezed: the picture (without the title bar) keeps 16:9.
        Assert.All(cells, c => Assert.InRange((c.Width - Frame.Width) / (double)(c.Height - Frame.Height), 16 / 9.0 - 0.01, 16 / 9.0 + 0.01));
        Assert.Single(cells.Select(c => c.Size).Distinct());
        // Nothing on anything else, the bars included.
        var withBars = cells.Select(c => new Rectangle(c.X, c.Y, c.Width, c.Height + Band)).ToList();
        for (int i = 0; i < withBars.Count; i++)
            for (int j = i + 1; j < withBars.Count; j++)
                Assert.False(withBars[i].IntersectsWith(withBars[j]));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(6, 3, 2)]
    [InlineData(9, 3, 3)]
    public void Games_make_a_grid_in_their_own_shape(int games, int columns, int rows)
    {
        var cells = WindowLayout.Grid(Work, games, Band, 16 / 9.0, Frame);
        Tidy(cells, games);
        Assert.Equal(rows, cells.Select(c => c.Top).Distinct().Count());
        Assert.Equal(columns, cells.Count(c => c.Top == cells[0].Top));
        Assert.Empty(WindowLayout.Grid(Work, 0, Band));
    }

    [Fact]
    public void Three_games_are_two_over_one_the_last_in_the_middle()
    {
        var cells = WindowLayout.Grid(Work, 3, Band, 16 / 9.0, Frame);
        Assert.True(cells[0].Width > 680);                     // larger than three side by side (640)
        int middle = cells[2].Left + cells[2].Width / 2;
        Assert.InRange(middle, 955, 965);
        Assert.Equal(cells[0].Top, cells[1].Top);
    }

    [Fact]
    public void The_shape_is_the_one_the_games_have_not_a_squeezed_one()
    {
        Assert.Equal(16 / 9.0, WindowLayout.Aspect([new Size(1204, 688), new Size(958, 398), new Size(756, 432)]), 3);
        Assert.Equal(4 / 3.0, WindowLayout.Aspect([new Size(1024, 768), new Size(800, 600)]), 3);
        Assert.Equal(16 / 9.0, WindowLayout.Aspect([new Size(958, 398)]), 3);
        Assert.Equal(16 / 9.0, WindowLayout.Aspect([]), 3);
    }

    [Fact]
    public void A_bar_goes_under_its_game_or_over_its_bottom_edge()
    {
        var work = new Rectangle(0, 0, 1920, 1032);
        var bar = new Size(420, 84);
        Assert.Equal(new Point(960, 946), WindowLayout.BarPlace(new Rectangle(960, 0, 960, 946), bar, work));     // room under it
        Assert.Equal(new Point(0, 948), WindowLayout.BarPlace(new Rectangle(0, 0, 1920, 1032), bar, work));       // no room: over the bottom
        Assert.Equal(new Point(1500, 500), WindowLayout.BarPlace(new Rectangle(1800, 100, 800, 400), bar, work)); // kept on the screen
    }
}
