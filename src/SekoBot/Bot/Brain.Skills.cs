using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Skills. Automatic (default): the skills on the quick slots are used by what the game says
/// they do — buffs are kept up (cast again shortly before they run out), area attacks when at
/// least AreaSkillMinMobs mobs are around, single attacks when a mob is in reach — each after
/// its cooldown and only with enough MP. Manual: F1–F5 pressed every N seconds.
/// Casting is cut off by walking or attacking, so the bot lets go of W/A/S/D and Space and holds
/// still for a moment while it casts; one skill at a time with a gap between them.
/// </summary>
internal sealed partial class Brain
{
    private readonly TimeSpan[] _lastSkill = new TimeSpan[BotProfile.SkillSlots];
    private readonly Dictionary<Keys, TimeSpan> _skillUsed = [];
    private TimeSpan _lastAnySkill = Never;

    private void ResetSkills()
    {
        Array.Fill(_lastSkill, Never);
        _skillUsed.Clear();
        _lastAnySkill = Never;
    }

    /// <returns>True when a skill was pressed this tick.</returns>
    private bool CastSkills(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (now - _lastAnySkill < Ms(p.SkillGapMs))
            return false;
        if (p.AutoSkills && state.Skills.Count > 0)
            return CastAutoSkill(state, p, actions);
        for (int i = 0; i < BotProfile.SkillSlots; i++)
        {
            int every = i < p.SkillEverySec.Length ? p.SkillEverySec[i] : 0;
            if (every <= 0 || i >= p.Keys.Skills.Length || now - _lastSkill[i] < Ms(every * 1000.0))
                continue;
            Cast(p.Keys.Skills[i], state, p, actions);
            _lastSkill[i] = now;
            return true;
        }
        return false;
    }

    private bool CastAutoSkill(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        double cx = state.Width / 2.0, cy = state.Height / 2.0, radius = p.AttackRadius * state.Height;
        double Distance(Point m) => Math.Sqrt(Math.Pow(m.X - cx, 2) + Math.Pow(m.Y - cy, 2));
        int around = state.MobCandidates.Count(m => Distance(m) <= radius * 2.5);
        bool inReach = state.MobCandidates.Any(m => Distance(m) <= radius * 1.4);

        foreach (var skill in state.Skills.OrderBy(s => s.Kind switch { SkillKind.Buff => 0, SkillKind.Area => 1, _ => 2 }))
        {
            bool wanted = skill.Kind switch
            {
                SkillKind.Buff => true,
                SkillKind.Area => around >= p.AreaSkillMinMobs,
                _ => inReach,
            };
            if (!wanted || state.MpPoints is int mp && mp < skill.Mana)
                continue;
            // A buff is cast again a little before it runs out; attacks as soon as they're ready.
            double wait = skill.Kind == SkillKind.Buff && skill.DurationSec > 0
                ? Math.Max(skill.CooldownSec, skill.DurationSec - 5)
                : skill.CooldownSec;
            if (_skillUsed.TryGetValue(skill.Key, out var used) && now - used < Ms((wait + 0.5) * 1000))
                continue;
            Cast(skill.Key, state, p, actions);
            _skillUsed[skill.Key] = now;
            return true;
        }
        return false;
    }

    private void Cast(Keys key, GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (p.NavigateMode)
        {
            SetMoves([], p, actions, refresh: false);
            SetAttack(false, p, actions, refresh: false);
            _pauseUntil = now + Ms(p.SkillCastMs); // stand still while casting
        }
        actions.Add(new KeyTapAction(key));
        _lastAnySkill = now;
    }
}
