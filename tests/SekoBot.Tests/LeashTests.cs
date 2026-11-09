using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class LeashTests
{
    /// <summary>A tiny game: held W/A/S/D move the character in the world, relative to the camera.</summary>
    private sealed class World(double yaw)
    {
        public Vec3 Pos = new(20000, 18000, 900);
        public readonly HashSet<Keys> Held = [];

        public void Apply(IEnumerable<BotAction> actions)
        {
            foreach (var a in actions)
            {
                if (a is KeyDownAction d) Held.Add(d.Key);
                if (a is KeyUpAction u) Held.Remove(u.Key);
            }
        }

        /// <summary>100 ms of walking: 30 units (3 m/s).</summary>
        public void Step()
        {
            double r = yaw * Math.PI / 180, fx = Math.Cos(r), fy = Math.Sin(r);
            double dx = 0, dy = 0;
            foreach (var key in Held)
                switch (key)
                {
                    case Keys.W: dx += fx; dy += fy; break;
                    case Keys.S: dx -= fx; dy -= fy; break;
                    case Keys.D: dx += -fy; dy += fx; break;
                    case Keys.A: dx -= -fy; dy -= fx; break;
                }
            Pos = Pos with { X = Pos.X + dx * 30, Y = Pos.Y + dy * 30 };
        }

        public GameState State(double ms) =>
            new(TimeSpan.FromMilliseconds(ms), 1920, 1080, 1, 1, 0, 1)
            {
                Position = Pos,
                Camera = new CameraView(Pos with { Z = Pos.Z + 1000 }, -80, yaw, 0, 90),
                Walking = Held.Count > 0,
            };
    }

    private static BotProfile Profile() => new() { SkillEverySec = new int[5], LeashMeters = 10 };

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
            world.Apply(actions);
            world.Step();
        }
        return all;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(-151.7)] // the camera as found in the game
    public void Walks_back_to_the_start_after_wandering_off(double yaw)
    {
        var p = Profile();
        var world = new World(yaw);
        var home = world.Pos;
        double furthestAfterReturn = 0;
        bool returned = false;
        var actions = Simulate(world, p, 40_000, t =>
        {
            if (t == 12_000) // carried far off (e.g. chasing mobs): 30 m away
                world.Pos = home with { X = home.X + 3000, Y = home.Y + 300 };
            if (t > 12_000)
            {
                double d = world.Pos.DistanceXY(home);
                if (d < 500) returned = true;
                if (returned) furthestAfterReturn = Math.Max(furthestAfterReturn, d);
            }
        });
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("Başlangıç yeri"));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("sınırı aşıldı"));
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("alanına dönüldü"));
        Assert.True(returned, "never came back");
        // Exploring drifts off again, but never far past the limit.
        Assert.True(furthestAfterReturn <= 1300, $"wandered {furthestAfterReturn:0} units after coming back");
    }

    [Fact]
    public void Fights_mobs_hitting_us_before_walking_back()
    {
        var p = Profile();
        var world = new World(0);
        var home = world.Pos;
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        List<BotAction> Run(double from, double to, TargetInfo[] mobs)
        {
            var all = new List<BotAction>();
            for (double t = from; t < to; t += 100)
            {
                var actions = brain.Tick(world.State(t) with { Targets = mobs, AttackPower = 189 });
                all.AddRange(actions);
                world.Apply(actions);
            }
            return all;
        }
        Run(0, 500, []);                                         // home taken
        world.Pos = home with { X = home.X + 3000 };             // 30 m off, past the 10 m limit
        // A mob 3 m away hits us (it chased us out here): fight it, don't walk home with it behind.
        var biter = new TargetInfo(1, new Point(960, 540 - 60), 300, 130, 103, 305, 305, 56, false, AttacksMe: true);
        var fight = Run(500, 3000, [biter]);
        Assert.Contains(fight, a => a is NoteAction n && n.Text.Contains("sınırı aşıldı"));
        Assert.Contains(fight, a => a is KeyDownAction d && d.Key == p.Keys.MoveForward);   // towards the mob (up on screen)
        Assert.DoesNotContain(fight, a => a is KeyDownAction d && d.Key == p.Keys.MoveBack);
        // Killed: now back home (camera yaw 0: home is behind → S).
        var back = Run(3000, 4000, []);
        Assert.Contains(back, a => a is KeyDownAction d && d.Key == p.Keys.MoveBack);
    }

    [Fact]
    public void Area_limit_off_means_no_home()
    {
        var p = Profile();
        p.LeashMeters = 0;
        var actions = Simulate(new World(0), p, 5_000);
        Assert.DoesNotContain(actions, a => a is NoteAction n && n.Text.Contains("Başlangıç yeri"));
    }
}

public class StuckTests
{
    private static (Brain, BotProfile) Ready()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
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
                MobCandidates = [mob], Walking = moving
            }));
        return actions;
    }

    [Fact]
    public void Steps_aside_when_the_character_does_not_move_while_walking()
    {
        var (brain, p) = Ready();
        var actions = Walk(brain, 4000, 6500, moving: false);
        Assert.Contains(actions, a => a is NoteAction n && n.Text.Contains("takıldı"));
        Assert.Contains(actions, a => a is KeyDownAction d && (d.Key == p.Keys.MoveForward || d.Key == p.Keys.MoveBack));
        // After the side step it heads for the mob again.
        var later = Walk(brain, 6500, 7000, moving: true);
        Assert.Contains(later, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }

    [Fact]
    public void Not_stuck_while_getting_going_or_when_the_mob_is_right_there()
    {
        var (brain, p) = Ready();
        // The first second after starting to walk (finishing a swing) doesn't count.
        var start = Walk(brain, 4000, 4900, moving: false);
        Assert.DoesNotContain(start, a => a is NoteAction n && n.Text.Contains("takıldı"));
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
