using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Skills. Automatic (default): the skills on the quick slots are used by what the game says
/// they do — buffs are kept up (cast again shortly before they run out), area attacks while
/// hitting with at least AreaSkillMinMobs mobs within 3 m, single attacks while hitting a mob — each after
/// its cooldown and only with enough MP. Manual: F1–F5 pressed every N seconds.
/// Casting is cut off by walking or attacking, so the bot lets go of W/A/S/D and Space and holds
/// still for a moment while it casts; one skill at a time with a gap between them.
/// </summary>
internal sealed partial class Brain
{
    private readonly TimeSpan[] _lastSkill = new TimeSpan[BotProfile.SkillSlots];
    private readonly Dictionary<Keys, TimeSpan> _skillUsed = [];
    private TimeSpan _lastAnySkill = Never;
    private Keys _lastSkillKey;
    private int _lastSkillSlot = -1;
    /// <summary>Learnt from the game's "WAIT!": how much longer each skill key needs than thought.</summary>
    private readonly Dictionary<Keys, TimeSpan> _skillExtra = [];

    /// <summary>Each "WAIT!" adds this much to the skill's wait (up to SkillExtraMaxMs).</summary>
    internal const int SkillExtraStepMs = 1000, SkillExtraMaxMs = 20000;

    private void ResetSkills()
    {
        Array.Fill(_lastSkill, Never);
        _skillUsed.Clear();
        _skillExtra.Clear();
        _lastAnySkill = Never;
        _lastSkillSlot = -1;
        _skillsTold = "";
    }

    /// <summary>The game said "WAIT!": the skill pressed last wasn't ready yet. It waits a little
    /// longer from now on (and from now), so its key isn't pressed for nothing again.</summary>
    private void SkillTooSoon(TimeSpan now, List<BotAction> actions)
    {
        if (_lastAnySkill == Never || now - _lastAnySkill > Ms(3000))
            return;
        var extra = _skillExtra.GetValueOrDefault(_lastSkillKey) + Ms(SkillExtraStepMs);
        if (extra > Ms(SkillExtraMaxMs))
            extra = Ms(SkillExtraMaxMs);
        if (!_skillExtra.ContainsKey(_lastSkillKey))
            actions.Add(new NoteAction($"Oyun \"WAIT!\" dedi: {_lastSkillKey} becerisi hazır olmadan basılmış; artık biraz daha seyrek basılacak."));
        _skillExtra[_lastSkillKey] = extra;
        _skillUsed[_lastSkillKey] = now;
        if (_lastSkillSlot >= 0)
            _lastSkill[_lastSkillSlot] = now;
    }

    /// <returns>True when a skill was pressed this tick.</returns>
    private bool CastSkills(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        if (now - _lastAnySkill < Ms(p.SkillGapMs))
            return false;
        if (p.AutoSkills && state.Skills.Count > 0)
        {
            TellSkills(state, actions);
            return CastAutoSkill(state, p, actions);
        }
        for (int i = 0; i < BotProfile.SkillSlots; i++)
        {
            int every = i < p.SkillEverySec.Length ? p.SkillEverySec[i] : 0;
            if (every <= 0 || i >= p.Keys.Skills.Length ||
                now - _lastSkill[i] < Ms(every * 1000.0) + _skillExtra.GetValueOrDefault(p.Keys.Skills[i]))
                continue;
            Cast(p.Keys.Skills[i], state, p, actions);
            _lastSkill[i] = now;
            _lastSkillSlot = i;
            return true;
        }
        return false;
    }

    /// <summary>Mobs this close (world units, 3 m) are hit by an area skill whose reach is unknown.</summary>
    internal const double AreaSkillRange = 300;
    /// <summary>An area skill's own reach counts up to this far (world units, 4 m).</summary>
    internal const double AreaSkillMaxRange = 400;
    /// <summary>Below this HP with two or more mobs on us, a stunning skill is used even on a small group.</summary>
    internal const double StunBelowHp = 0.5;
    /// <summary>A healing skill is used below this HP.</summary>
    internal const double HealBelowHp = 0.6;

    private string _skillsTold = "";

    /// <summary>The skills on the slots changed (first seen, learned, levelled, moved): say what
    /// each one does and how it will be used, and which learned ones the bot can't press.</summary>
    private void TellSkills(GameState state, List<BotAction> actions)
    {
        string told = string.Join("|", state.Skills.Select(s => $"{s.Key}{s.Name}{s.Level}{s.CooldownSec}{s.DurationSec}{s.Mana}")) +
                      "#" + string.Join("|", state.SkillsOffSlots);
        if (told == _skillsTold)
            return;
        _skillsTold = told;
        actions.Add(new NoteAction("Beceriler öğrenildi: " +
            string.Join(" · ", state.Skills.Select(s => $"{KeyName(s.Key)} {s.Name}{(s.Level > 0 ? $" (Sv {s.Level})" : "")}: {s.Describe()}")) + "."));
        if (state.SkillsOffSlots.Count > 0)
            actions.Add(new NoteAction($"Hızlı slotta olmayan beceri: {string.Join(", ", state.SkillsOffSlots)}. " +
                                       "Bot sadece 1–5 ve F1–F5 slotlarındaki becerileri kullanabilir; kullanılsın istersen boş bir slota koy."));
    }

    private static string KeyName(Keys key) => key is >= Keys.D0 and <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString();

    /// <summary>The character died: buffs are gone, so they are cast again as soon as possible.</summary>
    private void BuffsLost()
    {
        _skillUsed.Clear();
        Array.Fill(_lastSkill, Never);
    }

    private bool CastAutoSkill(GameState state, BotProfile p, List<BotAction> actions)
    {
        var now = state.Time;
        double cx = state.Width / 2.0, cy = state.Height / 2.0, radius = p.AttackRadius * state.Height;
        double Distance(Point m) => Math.Sqrt(Math.Pow(m.X - cx, 2) + Math.Pow(m.Y - cy, 2));
        // Attack skills only while really hitting (not while walking to a mob, gathering or with
        // mobs still on their way: the skill would hit nothing). Mobs from memory: real distances.
        bool hitting = !p.NavigateMode || _attackHeld && Phase == BotPhase.Fighting && !_pulling;
        bool fromMemory = state.Targets.Count > 0;
        // Each skill reaches as far as the game says (area ones up to 4 m around us).
        int Around(SkillSlot s)
        {
            if (!hitting)
                return 0;
            double reach = s.Range > 0 ? Math.Min(s.Range, AreaSkillMaxRange) : AreaSkillRange;
            return fromMemory ? state.Targets.Count(t => t.Distance <= reach) : state.MobCandidates.Count(m => Distance(m) <= radius * 2.5);
        }
        bool InReach(SkillSlot s) => hitting && (fromMemory
            ? state.Targets.Any(t => t.Distance <= Math.Max(t.Reach * 1.4, Math.Min(s.Range, AreaSkillMaxRange)))
            : state.MobCandidates.Any(m => Distance(m) <= radius * 1.4));
        // In trouble: HP low and several mobs hitting us. A stunning skill stops them for a moment.
        int onMe = fromMemory ? state.Targets.Count(t => t.AttacksMe && t.Distance <= AreaSkillMaxRange) : state.Attackers.Count;
        bool pressed = state.Hp is double hp && hp < StunBelowHp && onMe >= 2;
        // Stunning ones first when pressed; area attacks before the ones in front, those before single ones.
        int Order(SkillSlot s) => s.Kind switch
        {
            SkillKind.Heal => 0,
            SkillKind.Buff => 1,
            _ => (pressed && s.Traits.HasFlag(SkillTraits.Stun) ? 2 : 10) +
                 (s.Kind == SkillKind.Area ? 0 : s.Traits.HasFlag(SkillTraits.Front) ? 1 : 2),
        };

        foreach (var skill in state.Skills.OrderBy(Order))
        {
            bool wanted = skill.Kind switch
            {
                // A buff that slows us is put on in a fight, not while walking or gathering.
                SkillKind.Buff => !skill.Traits.HasFlag(SkillTraits.SlowsMe) || hitting,
                SkillKind.Heal => state.Hp is double h && h < HealBelowHp,
                SkillKind.Area => Around(skill) is var n && (n >= p.AreaSkillMinMobs || pressed && skill.Traits.HasFlag(SkillTraits.Stun) && n >= 2),
                _ => InReach(skill),
            };
            if (!wanted || state.MpPoints is int mp && mp < skill.Mana)
                continue;
            // A buff is cast again a little before it runs out; attacks as soon as they're ready.
            double wait = skill.Kind == SkillKind.Buff && skill.DurationSec > 0
                ? Math.Max(skill.CooldownSec, skill.DurationSec - 5)
                : skill.CooldownSec;
            if (_skillUsed.TryGetValue(skill.Key, out var used) &&
                now - used < Ms((wait + 0.5) * 1000) + _skillExtra.GetValueOrDefault(skill.Key))
                continue;
            Cast(skill.Key, state, p, actions);
            _lastSkillSlot = -1;
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
        _lastSkillKey = key;
    }
}
