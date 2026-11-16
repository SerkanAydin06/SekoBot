using System.Drawing;
using SekoBot.UI;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Several games on one screen: where the game windows and the bots' bars go.</summary>
public class WindowLayoutTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1032 - 86);

    [Fact]
    public void Two_games_go_side_by_side()
    {
        var cells = WindowLayout.Grid(Screen, 2);
        Assert.Equal([new Rectangle(0, 0, 960, 946), new Rectangle(960, 0, 960, 946)], cells);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    public void More_games_make_a_grid_that_fills_the_screen(int games, int rows)
    {
        var cells = WindowLayout.Grid(Screen, games);
        Assert.Equal(games, cells.Count);
        Assert.Equal(rows, cells.Select(c => c.Top).Distinct().Count());
        Assert.All(cells, c => Assert.True(Screen.Contains(c)));
        for (int i = 0; i < cells.Count; i++)
            for (int j = i + 1; j < cells.Count; j++)
                Assert.False(cells[i].IntersectsWith(cells[j]));
        Assert.Empty(WindowLayout.Grid(Screen, 0));
    }

    [Fact]
    public void Six_games_make_three_by_two_with_room_for_the_bars_under_every_row()
    {
        var work = new Rectangle(0, 0, 1920, 1032);
        var cells = WindowLayout.Grid(work, 6, 86);
        Assert.Equal(new Rectangle(0, 0, 640, 430), cells[0]);
        Assert.Equal(new Rectangle(1280, 516, 640, 430), cells[5]);
        // Every game's bar fits under it, on nothing else.
        var bars = cells.Select(c => new Rectangle(WindowLayout.BarPlace(c, new Size(c.Width, 84), work), new Size(c.Width, 84))).ToList();
        Assert.All(bars, b => Assert.DoesNotContain(cells, c => c.IntersectsWith(b)));
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
