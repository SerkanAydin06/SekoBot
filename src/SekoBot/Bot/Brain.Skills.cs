using SekoBot.Config;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Buff skills on F1–F5: each pressed again every N seconds (0 = off), one at a time with a
/// gap between them. Casting is cut off by walking or attacking, so the bot lets go of
/// W/A/S/D and Space and holds still for a moment while it casts.
/// </summary>
internal sealed partial class Brain
{
    private readonly TimeSpan[] _lastSkill = new TimeSpan[BotProfile.SkillSlots];
    private TimeSpan _lastAnySkill = Never;

    private void ResetSkills()
    {
        Array.Fill(_lastSkill, Never);
        _lastAnySkill = Never;
    }

    /// <returns>True when a skill was pressed this tick.</returns>
    private bool CastSkills(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (now - _lastAnySkill < Ms(p.SkillGapMs))
            return false;
        for (int i = 0; i < BotProfile.SkillSlots; i++)
        {
            int every = i < p.SkillEverySec.Length ? p.SkillEverySec[i] : 0;
            if (every <= 0 || i >= p.Keys.Skills.Length || now - _lastSkill[i] < Ms(every * 1000.0))
                continue;
            if (p.NavigateMode)
            {
                SetMoves([], p, actions, refresh: false);
                SetAttack(false, p, actions, refresh: false);
                _pauseUntil = now + Ms(p.SkillCastMs); // stand still while casting
            }
            actions.Add(new KeyTapAction(p.Keys.Skills[i]));
            _lastSkill[i] = now;
            _lastAnySkill = now;
            return true;
        }
        return false;
    }
}
