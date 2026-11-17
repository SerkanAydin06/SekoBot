using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Config
{
    /// <summary>How fights with one kind of mob went for a character (kept with the character).</summary>
    internal sealed class MobFights
    {
        /// <summary>Fights that kept HP above 70 %.</summary>
        public int Easy { get; set; }
        /// <summary>Fights with only a few mobs (at most <see cref="Bot.MobFit.FewMobs"/>) that took HP under 40 %.</summary>
        public int Hard { get; set; }
        public int Level { get; set; }
    }
}

namespace SekoBot.Bot
{
    /// <summary>
    /// Which mobs suit a character: learned from its own fights, with the level gap as the first
    /// guess. A kind that keeps taking a character's HP under 40 % even with only a few on it is too
    /// strong for it (gathering fewer won't help); one beaten easily again and again fits whatever
    /// its level. Before there's anything to go by: not more than <see cref="LevelAbove"/> levels
    /// above the character. Pure logic.
    /// </summary>
    internal static class MobFit
    {
        /// <summary>Not yet fought enough: mobs this many levels above the character are taken on, not more.</summary>
        public const int LevelAbove = 5;
        /// <summary>A hard fight counts against the mobs only with this few on us (more: too many, not too strong).</summary>
        public const int FewMobs = 4;
        /// <summary>Fights needed before what was learned counts.</summary>
        public const int Enough = 3;

        /// <summary>Too strong for the character (on what its fights showed, else on the level gap).</summary>
        public static bool TooHard(WorldMob mob, BotProfile profile, int level)
        {
            if (mob.IsMetin)
                return false;
            if (profile.MobFights.TryGetValue(mob.Name, out var f))
            {
                if (f.Hard >= Enough && f.Hard > f.Easy)
                    return true;
                if (f.Easy >= Enough && f.Hard * 3 <= f.Easy)
                    return false;
            }
            return level > 0 && mob.Level > 0 && mob.Level > level + LevelAbove;
        }

        /// <summary>A fight ended: the kinds that were on us go into the character's record.</summary>
        /// <returns>A new dictionary (the window may be saving the old one meanwhile).</returns>
        public static Dictionary<string, MobFights> Record(IReadOnlyDictionary<string, MobFights> fights,
            IEnumerable<(string Name, int Level)> kinds, int most, double lowestHp)
        {
            bool hard = lowestHp < Brain.PullDangerHp && most <= FewMobs;
            bool easy = lowestHp > Brain.PullSafeHp;
            var copy = fights.ToDictionary(f => f.Key, f => new MobFights { Easy = f.Value.Easy, Hard = f.Value.Hard, Level = f.Value.Level });
            if (!hard && !easy)
                return copy;
            foreach (var (name, mobLevel) in kinds.Distinct())
            {
                if (!copy.TryGetValue(name, out var f))
                    copy[name] = f = new MobFights();
                f.Level = mobLevel;
                if (hard) f.Hard++;
                else f.Easy++;
                // Recent fights count most: halve old counts once they get large (the character grows).
                if (f.Easy + f.Hard > 20)
                {
                    f.Easy /= 2;
                    f.Hard /= 2;
                }
            }
            return copy;
        }
    }

    /// <summary>
    /// Watches the fights (mobs from memory that attack us): which kinds, how many at most and the
    /// lowest HP. A fight is over when nothing has attacked for <see cref="Quiet"/>. Pure logic.
    /// </summary>
    internal sealed class FightWatch
    {
        public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(3);
        private readonly HashSet<(string, int)> _kinds = [];
        private int _most;
        private double _lowest = 1;
        private TimeSpan _lastAttack;

        /// <returns>The fight that just ended, if one did.</returns>
        public (List<(string Name, int Level)> Kinds, int Most, double LowestHp)? Update(IEnumerable<WorldMob> mobs, double? hp, TimeSpan now)
        {
            var on = mobs.Where(m => m.AttacksMe && !m.IsMetin).ToList();
            if (on.Count > 0)
            {
                foreach (var m in on)
                    _kinds.Add((m.Name, m.Level));
                _most = Math.Max(_most, on.Count);
                if (hp is double h)
                    _lowest = Math.Min(_lowest, h);
                _lastAttack = now;
                return null;
            }
            if (_kinds.Count == 0 || now - _lastAttack < Quiet)
                return null;
            var fight = (_kinds.ToList(), _most, _lowest);
            _kinds.Clear();
            _most = 0;
            _lowest = 1;
            return fight;
        }

        public void Reset()
        {
            _kinds.Clear();
            _most = 0;
            _lowest = 1;
        }
    }
}
