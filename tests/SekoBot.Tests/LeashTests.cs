using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Recording;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class MapReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Finds_the_big_map_and_our_arrow_on_it()
    {
        using var frame = RecordingFormat.LoadPng(Fixture("big-map.png"));
        var map = MapReader.Read(frame);
        Assert.NotNull(map);
        Assert.InRange(map!.Value.Panel.X, 1316, 1336);
        Assert.InRange(map.Value.Panel.Width, 340, 400);
        var arrow = Assert.NotNull(map.Value.Arrow);
        // Arrow at about (1443, 316) on the screen.
        Assert.InRange(arrow.X * map.Value.Panel.Width + map.Value.Panel.X, 1433, 1453);
        Assert.InRange(arrow.Y * map.Value.Panel.Width + map.Value.Panel.Y, 306, 326);
    }

    [Theory]
    [InlineData("topdown-wolves.png")]
    [InlineData("red-armor.png")]
    [InlineData("other-player.png")]
    [InlineData("levelup-popup.png")]
    [InlineData("esc-menu.png")]
    [InlineData("death-screen.png")]
    public void No_map_on_other_screens(string file)
    {
        using var frame = RecordingFormat.LoadPng(Fixture(file));
        Assert.Null(MapReader.Read(frame));
    }
}

public class LeashTests
{
    /// <summary>A tiny game: M toggles the map, held W/A/S/D move the character on it.</summary>
    private sealed class World(Func<Keys, (double X, double Y)?> direction)
    {
        public PointF Pos = new(0.30f, 0.40f);
        public bool MapOpen;
        public bool MapWorks = true;
        /// <summary>The position is also known from memory (after "Koordinat bul").</summary>
        public bool Live;
        public bool Started; // the character stands still until the bot first looks at the map
        public int MapTaps;
        public readonly HashSet<Keys> Held = [];

        public void Apply(IEnumerable<BotAction> actions, BotProfile p)
        {
            foreach (var a in actions)
            {
                switch (a)
                {
                    case KeyTapAction t when t.Key == p.Keys.Map:
                        MapTaps++;
                        Started = true;
                        if (MapWorks) MapOpen = !MapOpen;
                        break;
                    case KeyDownAction d: Held.Add(d.Key); break;
                    case KeyUpAction u: Held.Remove(u.Key); break;
                }
            }
        }

        public void Step()
        {
            if (!Started)
                return;
            foreach (var key in Held)
                if (direction(key) is (double x, double y))
                    Pos = new PointF(Pos.X + (float)(x * 0.002), Pos.Y + (float)(y * 0.002));
        }

        public GameState State(double ms) =>
            new(TimeSpan.FromMilliseconds(ms), 1920, 1080, 1, 1, null, 1)
            {
                MapOpen = MapOpen,
                MapPosition = MapOpen ? Pos : null,
                LivePosition = Live ? Pos : null
            };
    }

    private static BotProfile Profile() => new()
    {
        SkillEverySec = new int[5], LeashRadius = 0.06, LeashCheckMs = 3000
    };

    private static double Distance(PointF a, PointF b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static List<BotAction> Simulate(World world, BotProfile p, double until, Action<double>? at = null)
    {
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var all = new List<BotAction>();
        for (double t = 0; t < until; t += 100)
        {
            at?.Invoke(t);
            var actions = brain.Tick(world.State(t));
            all.AddRange(actions);
            world.Apply(actions, p);
            world.Step();
        }
        return all;
    }

    private static (double X, double Y)? NorthUp(Keys k) => k switch
    {
        Keys.W => (0, -1), Keys.S => (0, 1), Keys.A => (-1, 0), Keys.D => (1, 0), _ => null
    };

    /// <summary>The camera turned 90°: walking "up" on screen goes east on the map.</summary>
    private static (double X, double Y)? Turned(Keys k) => k switch
    {
        Keys.W => (1, 0), Keys.D => (0, 1), Keys.S => (-1, 0), Keys.A => (0, -1), _ => null
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Walks_back_to_the_start_after_wandering_off(bool turnedCamera)
    {
        var p = Profile();
        var world = new World(turnedCamera ? Turned : NorthUp);
        var home = world.Pos;
        double closestAfterWandering = double.MaxValue, furthestAfterReturn = 0;
        bool returned = false;
        var actions = Simulate(world, p, 45_000, t =>
        {
            if (t == 12_000) // carried far off (e.g. chasing mobs)
                world.Pos = new PointF(home.X + 0.10f, home.Y + 0.01f);
            if (t > 12_000)
            {
                double d = Distance(world.Pos, home);
                closestAfterWandering = Math.Min(closestAfterWandering, d);
                if (d < 0.035) returned = true;
                if (returned) furthestAfterReturn = Math.Max(furthestAfterReturn, d);
            }
        });
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("Başlangıç yeri"));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("yönü ayarlandı"));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("sınırı aşıldı"));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("alanına dönüldü"));
        Assert.True(returned, $"never came back (closest {closestAfterWandering:0.000})");
        // Exploring drifts between two checks (every 3 s here), but it is always pulled back.
        Assert.True(furthestAfterReturn <= 0.13, $"wandered {furthestAfterReturn:0.000} after coming back");
        Assert.False(world.MapOpen); // always closed again
    }

    [Fact]
    public void With_the_position_from_memory_the_map_is_only_opened_once()
    {
        var p = Profile();
        var world = new World(NorthUp) { Live = true };
        var home = world.Pos;
        bool returned = false;
        var actions = Simulate(world, p, 45_000, t =>
        {
            if (t == 12_000)
                world.Pos = new PointF(home.X + 0.10f, home.Y + 0.01f);
            if (t > 12_000 && Distance(world.Pos, home) < 0.035)
                returned = true;
        });
        Assert.Equal(2, world.MapTaps); // opened once to check the start, then closed for good
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("yönü ayarlandı"));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("sınırı aşıldı"));
        Assert.True(returned);
        Assert.False(world.MapOpen);
    }

    [Fact]
    public void A_map_that_does_not_open_is_not_pressed_again_right_away()
    {
        var p = Profile();
        var world = new World(NorthUp) { MapWorks = false };
        var actions = Simulate(world, p, 5_500);
        Assert.Equal(1, world.MapTaps);
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("açılmadı"));
    }

    [Fact]
    public void Area_limit_off_means_no_map()
    {
        var p = Profile();
        p.LeashRadius = 0;
        var world = new World(NorthUp);
        Simulate(world, p, 20_000);
        Assert.Equal(0, world.MapTaps);
    }
}

public class StuckTests
{
    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashRadius = 0, SkillEverySec = new int[5] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        brain.Tick(new GameState(TimeSpan.Zero, 1920, 1080, 1, 1, null, 1));
        brain.Tick(new GameState(TimeSpan.FromMilliseconds(3500), 1920, 1080, 1, 1, null, 1));
        return (brain, p);
    }

    private static List<BotAction> Walk(Brain brain, double from, double to, bool moving)
    {
        var mob = new Point(960 + 600, 540); // to the right
        var actions = new List<BotAction>();
        for (double t = from; t < to; t += 100)
            actions.AddRange(brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), 1920, 1080, 1, 1, null, 1)
            {
                MobCandidates = [mob], ScreenMoving = moving
            }));
        return actions;
    }

    [Fact]
    public void Steps_aside_when_the_picture_does_not_move_while_walking()
    {
        var (brain, p) = Ready();
        var actions = Walk(brain, 4000, 5500, moving: false);
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("takıldı"));
        Assert.Contains(actions, a => a is KeyDownAction d && (d.Key == p.Keys.MoveForward || d.Key == p.Keys.MoveBack));
        // After the side step it heads for the mob again.
        var later = Walk(brain, 5500, 6000, moving: true);
        Assert.Contains(later, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void No_side_steps_while_walking_freely()
    {
        var (brain, p) = Ready();
        var actions = Walk(brain, 4000, 8000, moving: true);
        Assert.DoesNotContain(actions, a => a is NoteAction n && n.Text.Contains("takıldı"));
        Assert.DoesNotContain(actions, a => a is KeyDownAction d && (d.Key == p.Keys.MoveForward || d.Key == p.Keys.MoveBack));
    }
}
