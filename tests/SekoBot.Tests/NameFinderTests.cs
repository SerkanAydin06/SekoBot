using SekoBot.Capture;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class NameFinderTests
{
    private const int W = 800, H = 600;

    private sealed class Canvas
    {
        public readonly byte[] Data = new byte[W * H * 4];

        public Canvas()
        {
            // Greenish textured ground: never "red".
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                byte v = (byte)(60 + ((x * 37 + y * 91) ^ (x * y)) % 120);
                Data[i] = (byte)(v / 2); Data[i + 1] = v; Data[i + 2] = (byte)(v * 3 / 4);
            }
        }

        public void Fill(int x0, int y0, int w, int h, byte r = 220, byte g = 40, byte b = 40)
        {
            for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                int i = (y * W + x) * 4;
                Data[i] = b; Data[i + 1] = g; Data[i + 2] = r;
            }
        }

        /// <summary>Like in the game: a dark red label box with light pink text inside.</summary>
        public void Label(int x0, int y0, int w)
        {
            LevelTag(x0, y0 + 3);
            Fill(x0, y0, w, 16, 120, 30, 30);
            for (int k = 0; k + 7 < w - 4; k += 9)
                Fill(x0 + 3 + k, y0 + 3, 5, 10, 250, 150, 150);
        }

        /// <summary>The green "Lv.6" in front of every mob name.</summary>
        public void LevelTag(int nameLeft, int y0)
        {
            for (int k = 0; k < 3; k++)
                Fill(nameLeft - 30 + k * 8, y0 + 1, 2, 9, 30, 125, 20);
        }

        /// <summary>A red mob name ("F"-like letters, 10 px tall, 9 px apart) with its level tag.</summary>
        public void Name(int x0, int y0, int letters, bool levelTag = true)
        {
            if (levelTag)
                LevelTag(x0, y0);
            for (int k = 0; k < letters; k++)
            {
                int lx = x0 + k * 9;
                Fill(lx, y0, 2, 10);
                Fill(lx, y0, 7, 2);
                Fill(lx, y0 + 4, 5, 2);
            }
        }

        public Frame ToFrame() => Frame.FromArray(W, H, Data);
    }

    [Fact]
    public void Finds_red_names_but_not_bars_or_ui_buttons()
    {
        var c = new Canvas();
        c.Name(250, 150, 8);     // mob
        c.Name(600, 420, 5);     // another mob
        c.Fill(520, 250, 100, 6);  // thin solid red strip: a bar, not a name
        c.Label(150, 380, 40);     // "Kurt" on its dark red label
        c.Name(60, 200, 3);      // red "Görevler" button text, top left: UI
        c.Fill(20, 560, 120, 6); // HP bar in the HUD
        using var frame = c.ToFrame();
        var names = NameFinder.Find(frame);
        Assert.Equal(3, names.Count);
        Assert.Contains(names, n => Math.Abs(n.X - 150) <= 4 && Math.Abs(n.Y - 380) <= 4);
        Assert.Contains(names, n => Math.Abs(n.X - 250) <= 4 && Math.Abs(n.Y - 150) <= 4 && n.Width >= 64);
        Assert.Contains(names, n => Math.Abs(n.X - 600) <= 4 && Math.Abs(n.Y - 420) <= 4);
    }

    [Fact]
    public void Plain_ground_has_no_names()
    {
        using var frame = new Canvas().ToFrame();
        Assert.Empty(NameFinder.Find(frame));
    }

    [Fact]
    public void Click_goes_below_the_name_onto_the_moving_mob_when_there_is_one()
    {
        var name = new Rectangle(250, 150, 72, 12);
        // Moving thing just below the name, plus an unrelated one far away.
        var motion = new List<Point> { new(700, 450), new(290, 200) };
        var spots = MobLocator.Combine([name], motion, W, H);
        Assert.Equal(new Point(290, 200), spots[0]);
        Assert.Equal(new Point(700, 450), spots[1]);
        Assert.Equal(2, spots.Count);
    }

    [Fact]
    public void Without_motion_click_a_fixed_distance_below_the_name()
    {
        var name = new Rectangle(250, 150, 72, 12);
        var spots = MobLocator.Combine([name], [], W, H);
        var spot = Assert.Single(spots);
        Assert.Equal(286, spot.X);
        Assert.InRange(spot.Y, name.Bottom + 25, name.Bottom + 70);
    }

    [Fact]
    public void Names_come_before_motion_nearest_first()
    {
        var near = new Rectangle(330, 200, 60, 12);
        var far = new Rectangle(60, 100, 60, 12);
        var spots = MobLocator.Combine([far, near], [new(420, 330)], W, H);
        Assert.Equal(3, spots.Count);
        Assert.Equal(360, spots[0].X); // near name
        Assert.Equal(90, spots[1].X);  // far name
        Assert.Equal(new Point(420, 330), spots[2]); // plain motion last
    }

    /// <summary>Calm grass (little noise), so the mob's body stands out from it.</summary>
    private static Frame Grass(Action<byte[]>? draw = null)
    {
        var data = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int i = (y * W + x) * 4;
            int n = ((x * 7 + y * 13) ^ (x * y)) % 11 - 5;
            data[i] = (byte)(40 + n); data[i + 1] = (byte)(120 + n); data[i + 2] = (byte)(60 + n);
        }
        draw?.Invoke(data);
        return Frame.FromArray(W, H, data);
    }

    private static void Block(byte[] data, int x0, int y0, int w, int h, byte gray)
    {
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++)
        {
            int i = (y * W + x) * 4;
            data[i] = gray; data[i + 1] = gray; data[i + 2] = gray;
        }
    }

    [Fact]
    public void Finds_the_mob_body_under_its_name()
    {
        var name = new Rectangle(250, 150, 72, 12);
        // A dark grey "wolf" a little below the name.
        using var frame = Grass(d => Block(d, 260, 190, 50, 30, 50));
        var body = MobLocator.BodyBelow(frame, name);
        Assert.NotNull(body);
        Assert.InRange(body!.Value.X, 265, 305);
        Assert.InRange(body.Value.Y, 192, 218);

        // Used for the click when nothing moves there.
        var spots = MobLocator.Combine([name], [], W, H, n => MobLocator.BodyBelow(frame, n));
        Assert.Equal(body.Value, spots[0]);
    }

    [Fact]
    public void No_body_on_empty_grass()
    {
        using var frame = Grass();
        Assert.Null(MobLocator.BodyBelow(frame, new Rectangle(250, 150, 72, 12)));
    }

    [Fact]
    public void Real_screenshot_finds_the_three_wolves_and_nothing_else()
    {
        // 1919×1078 top-down screenshot from the game: "Aç Mavi Kurt" ×2 and "Kurt" near the
        // bottom; red "Görevler"/"Mesaj" buttons on the left must not count.
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "topdown-wolves.png");
        using var frame = SekoBot.Recording.RecordingFormat.LoadPng(path);
        var names = NameFinder.Find(frame);
        Assert.Equal(3, names.Count);
        Assert.Contains(names, n => Math.Abs(n.X - 368) <= 8 && Math.Abs(n.Y - 860) <= 8);
        Assert.Contains(names, n => Math.Abs(n.X - 536) <= 8 && Math.Abs(n.Y - 868) <= 8);
        Assert.Contains(names, n => Math.Abs(n.X - 860) <= 8 && Math.Abs(n.Y - 888) <= 8);
        Assert.DoesNotContain(names, n => n.X < 250);
    }

    [Fact]
    public void Red_text_without_a_level_tag_is_not_a_mob()
    {
        var c = new Canvas();
        c.Name(250, 150, 8);                    // real mob name
        c.Name(420, 290, 3, levelTag: false);   // floating damage number near the character
        c.Name(380, 330, 6, levelTag: false);   // something red over the character
        using var frame = c.ToFrame();
        var names = NameFinder.Find(frame);
        var name = Assert.Single(names);
        Assert.InRange(name.X, 246, 254);
        // The check can be switched off.
        Assert.Equal(3, NameFinder.Find(frame, requireLevelTag: false).Count);
    }

    [Fact]
    public void Reddish_hair_under_the_players_own_lv_tag_is_not_a_mob()
    {
        var c = new Canvas();
        c.Name(250, 150, 8); // real mob name, to be sure the frame works
        // The player's own name line: green "Lv.8" (the name itself is white, not drawn), and
        // right under it the character's reddish-orange head, wide enough to look like text.
        c.LevelTag(430, 280);
        c.Fill(440, 290, 28, 12, 200, 90, 60);
        using var frame = c.ToFrame();
        var name = Assert.Single(NameFinder.Find(frame));
        Assert.InRange(name.X, 246, 254);
    }

    [Theory]
    [InlineData(130, 34, 32, true)]   // median name red on the real screenshot
    [InlineData(189, 35, 37, true)]   // bright core of the letters
    [InlineData(100, 12, 14, true)]   // dark edge of the letters
    [InlineData(200, 90, 60, false)]  // reddish-orange hair / skin
    [InlineData(230, 120, 40, false)] // orange (fire, gold)
    [InlineData(250, 150, 150, false)] // pink
    [InlineData(70, 15, 15, false)]   // too dark
    public void Name_red_is_the_measured_pure_red(int r, int g, int b, bool expected) =>
        Assert.Equal(expected, NameFinder.IsNameRed(r, g, b));

    [Fact]
    public void Red_armour_on_the_players_character_is_not_a_mob()
    {
        // Real 1919×1079 screenshot: the character wears red armour right under "Lv.6 KAIZER06"
        // in the middle; 15 mob names around it have a visible Lv tag.
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "red-armor.png");
        using var frame = SekoBot.Recording.RecordingFormat.LoadPng(path);
        var names = NameFinder.Find(frame);
        Assert.DoesNotContain(names, n => Math.Abs(n.X + n.Width / 2 - 960) < 60 && Math.Abs(n.Y + n.Height / 2 - 545) < 40);
        Assert.InRange(names.Count, 14, 16);
        Assert.Contains(names, n => Math.Abs(n.X - 972) <= 8 && Math.Abs(n.Y - 260) <= 8);  // Yabani Köpek above
        Assert.Contains(names, n => Math.Abs(n.X - 756) <= 8 && Math.Abs(n.Y - 420) <= 8);  // nearest one, up-left
    }

    [Fact]
    public void Another_players_red_armour_is_not_a_mob()
    {
        // Real screenshot with another player ("Lv.3 azad06", red armour) next to the wolves.
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "other-player.png");
        using var frame = SekoBot.Recording.RecordingFormat.LoadPng(path);
        // Make it hard: paint a block of exactly the name red onto that player's armour.
        for (int y = 448; y < 462; y++)
        for (int x = 672; x < 712; x++)
        {
            int i = y * frame.Stride + x * 4;
            frame.Data[i] = 30; frame.Data[i + 1] = 30; frame.Data[i + 2] = 150;
        }
        var names = NameFinder.Find(frame);
        Assert.DoesNotContain(names, n => Math.Abs(n.X + n.Width / 2 - 692) < 40 && Math.Abs(n.Y + n.Height / 2 - 455) < 25);
        Assert.DoesNotContain(names, n => Math.Abs(n.X + n.Width / 2 - 965) < 40 && Math.Abs(n.Y + n.Height / 2 - 548) < 25);
        Assert.Equal(8, names.Count);
        // The wolf whose name is half hidden behind an icon next to that player still counts.
        Assert.Contains(names, n => Math.Abs(n.X - 788) <= 8 && Math.Abs(n.Y - 468) <= 8);
    }

    [Fact]
    public void Red_in_the_middle_without_a_tag_on_its_line_is_our_own_character_even_without_a_visible_name()
    {
        var c = new Canvas();
        c.Name(250, 150, 8);                // a real mob
        c.LevelTag(380, 290);               // our "Lv.6" (our white name not recognisable)
        c.Fill(395, 296, 30, 12, 220, 40, 40); // red armour in the middle, a bit below the tag line
        using var frame = c.ToFrame();
        var names = NameFinder.Find(frame);
        var only = Assert.Single(names);
        Assert.InRange(only.X, 246, 254);
    }

    [Fact]
    public void A_mob_right_next_to_us_is_still_found()
    {
        var c = new Canvas();
        c.Name(410, 300, 6); // mob name with its tag on the line, right at the middle
        using var frame = c.ToFrame();
        Assert.Single(NameFinder.Find(frame));
    }
}
