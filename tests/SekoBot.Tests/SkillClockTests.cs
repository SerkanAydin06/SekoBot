using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Skills timed by the game's own quick slot clock (how long each slot still cools down).</summary>
public class SkillClockTests
{
    private const int W = 1920, H = 1080;

    /// <summary>A pretend game: a press that "takes" starts the slot's cooldown.</summary>
    private sealed class Game(int cooldownSec, int ignorePresses = 0)
    {
        private double _castAt = double.NegativeInfinity;
        private int _ignore = ignorePresses;
        public int Presses, Casts;
        public double Wait(double ms) => Math.Max(0, cooldownSec - (ms - _castAt) / 1000);
        public void Pressed(double ms)
        {
            Presses++;
            if (_ignore-- > 0)
                return;
            Casts++;
            _castAt = ms;
        }
        public void CastBefore(double secondsAgo) => _castAt = -secondsAgo * 1000;
    }

    private static Brain NewBrain()
    {
        var p = new BotProfile { LeashMeters = 0 };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        return brain;
    }

    // A buff like "Hava kılıcı": 94 s on, every 49 s.
    private static List<BotAction> Run(Brain brain, Game game, double from, double to)
    {
        var all = new List<BotAction>();
        for (double t = from; t < to; t += 100)
        {
            var skill = new SkillSlot(Keys.F1, "Hava kılıcı", SkillKind.Buff, 168, 49, 94, Wait: game.Wait(t));
            var state = new GameState(TimeSpan.FromMilliseconds(t), W, H, 1, 1, 0, 1) { Skills = [skill], MpPoints = 1000, Walking = true };
            var actions = brain.Tick(state);
            if (actions.OfType<KeyTapAction>().Any(k => k.Key == Keys.F1))
                game.Pressed(t);
            all.AddRange(actions);
        }
        return all;
    }

    [Fact]
    public void A_buff_is_not_pressed_again_when_its_cooldown_ends_but_when_it_runs_out()
    {
        var brain = NewBrain();
        var game = new Game(49);
        Run(brain, game, 0, 85_000);           // cooldown over at 49 s; the buff still lasts until 94 s
        Assert.Equal(1, game.Presses);
        Run(brain, game, 85_000, 95_000);      // shortly before it runs out: again
        Assert.Equal(2, game.Presses);
    }

    [Fact]
    public void A_press_that_did_not_take_is_tried_again_soon()
    {
        var brain = NewBrain();
        var game = new Game(49, ignorePresses: 1);
        var actions = Run(brain, game, 0, 8_000);
        Assert.Equal(2, game.Presses);
        Assert.Equal(1, game.Casts);
        Assert.Contains(actions.OfType<NoteAction>(), n => n.Text.Contains("kullanılmadı"));
        Run(brain, game, 8_000, 60_000);       // then it holds
        Assert.Equal(2, game.Presses);
    }

    [Fact]
    public void A_buff_put_on_before_the_bot_started_is_left_alone_until_it_runs_out()
    {
        var brain = NewBrain();
        var game = new Game(49);
        game.CastBefore(20);                    // on for 74 s more
        Run(brain, game, 0, 60_000);
        Assert.Equal(0, game.Presses);
        Run(brain, game, 60_000, 80_000);
        Assert.Equal(1, game.Presses);
    }

    [Fact]
    public void A_skill_cut_off_by_the_casting_before_it_is_learned_for_that_skill_by_everyone()
    {
        var lore = new SkillLore();
        var notes = new List<string>();
        // One character (one SekoBot) for 30 s; the game: a skill pressed less than 2.5 s after the
        // one before is cut off (long casting). How many presses were cut off.
        int Character()
        {
            var p = new BotProfile { LeashMeters = 0 };
            var brain = new Brain(() => p) { Lore = lore };
            brain.Start(TimeSpan.Zero);
            double zirhAt = double.NegativeInfinity, keskinAt = double.NegativeInfinity, lastPress = double.NegativeInfinity;
            int cutOff = 0;
            for (double t = 0; t < 30_000; t += 100)
            {
                double Wait(double at, int cd) => Math.Max(0, cd - (t - at) / 1000);
                SkillSlot[] skills =
                [
                    new(Keys.F1, "Büyülü zırh", SkillKind.Buff, 78, 40, 72, Wait: Wait(zirhAt, 40)),
                    new(Keys.F2, "Büyülü keskinlik", SkillKind.Buff, 44, 20, 120, Wait: Wait(keskinAt, 20)),
                ];
                var actions = brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), W, H, 1, 1, 0, 1) { Skills = skills, MpPoints = 1000, Walking = true });
                notes.AddRange(actions.OfType<NoteAction>().Select(n => n.Text));
                foreach (var key in actions.OfType<KeyTapAction>().Select(k => k.Key))
                {
                    if (key is not (Keys.F1 or Keys.F2))
                        continue;
                    bool cut = t - lastPress < 2500;
                    lastPress = t;
                    if (cut) { cutOff++; continue; }
                    if (key == Keys.F1) zirhAt = t; else keskinAt = t;
                }
            }
            Assert.True(keskinAt > 0 && zirhAt >= 0);       // both on in the end
            Assert.Equal(0, p.SkillGapLearnedMs);            // not kept for this character only
            return cutOff;
        }
        int first = Character(), second = Character(), third = Character();
        Assert.True(first >= 1);
        Assert.True(second <= first);
        Assert.Equal(0, third);                              // learned: no more cut-offs, whoever uses it
        Assert.True(lore.CastMs("Büyülü zırh") >= 2500);    // for the skill whose casting takes long
        Assert.Contains(notes, n => n.Contains("bütün karakterler"));
    }

    [Fact]
    public void What_is_learned_about_skills_is_shared_by_the_SekoBots()
    {
        string path = Path.Combine(Path.GetTempPath(), "sekobot-lore-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var ninjaBot = SkillLore.Load(path);
            var otherBot = SkillLore.Load(path);
            ninjaBot.CutOffAfter("Pusu", 1500);
            ninjaBot.TooSoon("Zehirli bulut");
            ninjaBot.Save(path);
            otherBot.Save(path);
            Assert.Equal(2000, otherBot.CastMs("Pusu"));
            Assert.Equal(SkillLore.ExtraStepMs, otherBot.ExtraMs("Zehirli bulut"));
            Assert.Equal(2000, SkillLore.Load(path).CastMs("Pusu"));   // kept for the next start
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Quick_slot_waits_reach_the_skill_keys()
    {
        var world = new WorldSnapshot(new Vec3(0, 0, 0), null, [], 0, "c2",
            Progress: new CharacterProgress(0, 1, 0, [], [0, 0, 0, 0, 0, -104, -103]),
            Skills: [new SkillInfo(4, "Hava kılıcı", 16, SkillKind.Buff, 168, 49, 94, 0),
                     new SkillInfo(3, "Öfke", 1, SkillKind.Buff, 57, 66, 128, 0)],
            SkillWaits: [null, null, null, null, null, 12.5, 0]);
        var slots = BotEngine.SkillKeys(world, new BotProfile());
        Assert.Equal([(Keys.F1, (double?)12.5), (Keys.F2, 0)], slots.Select(s => (s.Key, s.Wait)));
    }

    [Fact]
    public void A_learned_wait_after_the_cooldown_is_kept_and_wears_off_when_not_needed()
    {
        var lore = new SkillLore();
        for (int i = 0; i < 4; i++)
            lore.TooSoon("Ruh vuruşu");                         // 2 s after its cooldown
        var p = new BotProfile { LeashMeters = 0 };
        var brain = new Brain(() => p) { Lore = lore };
        brain.Start(TimeSpan.Zero);
        // An attack skill, 10 s cooldown, read from the game's clock.
        double castAt = double.NegativeInfinity;
        var presses = new List<double>();
        for (double t = 0; t < 40_000; t += 100)
        {
            double wait = Math.Max(0, 10 - (t - castAt) / 1000);
            var skill = new SkillSlot(Keys.F3, "Ruh vuruşu", SkillKind.Area, 50, 10, 0, Wait: wait);
            TargetInfo[] mobs = [.. Enumerable.Range(1, 6).Select(i => new TargetInfo(i, new Point(970 + i, 540), 60, 130, 365, 600, 600, 50, false, AttacksMe: true))];
            var actions = brain.Tick(new GameState(TimeSpan.FromMilliseconds(t), W, H, 1, 1, 0, 1) { Skills = [skill], MpPoints = 1000, Walking = true, Targets = mobs, AttackPower = 189 });
            if (actions.OfType<KeyTapAction>().Any(k => k.Key == Keys.F3))
            {
                presses.Add(t);
                castAt = t;
            }
        }
        Assert.True(presses.Count >= 2);
        // Pressed again not when the slot is ready (10 s) but about 2 s after.
        Assert.All(presses.Zip(presses.Skip(1), (a, b) => b - a), gap => Assert.True(gap >= 11_500));
        Assert.True(lore.ExtraMs("Ruh vuruşu") < 2000);         // each press that took eased it
    }

    [Fact]
    public void A_miss_for_another_reason_does_not_make_the_casting_longer_and_longer()
    {
        var lore = new SkillLore();
        lore.CutOffAfter("Güçlü vuruş", 1500);                 // cut off at 1.5 s: wait 2 s after it
        Assert.Equal(2000, lore.CastMs("Güçlü vuruş"));
        lore.TookAfter("Güçlü vuruş", 2000);                   // took at 2 s
        for (int i = 0; i < 10; i++)
            lore.CutOffAfter("Güçlü vuruş", 2000 + i * 100);   // misses later on: no MP, the mob gone…
        Assert.Equal(2000, lore.CastMs("Güçlü vuruş"));        // not up to 4 s
        lore.TookAfter("Güçlü vuruş", 1400);                   // even 1.4 s works: the 1.5 s "cut-off" was something else
        Assert.True(lore.CastMs("Güçlü vuruş") <= 1400);
    }
}
