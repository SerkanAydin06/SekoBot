using SekoBot.Memory;

namespace SekoBot.Vision;

/// <summary>A skill on a quick slot key, with what the game says about it.</summary>
internal sealed record SkillSlot(Keys Key, string Name, SkillKind Kind, int Mana, int CooldownSec, int DurationSec);

/// <summary>A mob the bot may go for: where it is on screen, how far (world units), from how far
/// it can be hit, and what killing it is worth.</summary>
/// <param name="Reach">Centre-to-centre distance from which the character hits it.</param>
/// <param name="Priority">A mob to go for first (attacking us, or a preferred Metin stone).</param>
/// <param name="AttacksMe">The mob is after us.</param>
/// <param name="Group">The spawn group it belongs to (mobs of a group come together when one is hit); 0 = none.</param>
/// <param name="Position">Where it is in the world, when known.</param>
internal sealed record TargetInfo(int Id, Point Spot, double Distance, double Reach, int Exp, int Hp, int MaxHp,
    int DamageMax, bool Priority, bool AttacksMe = false, int Group = 0, Vec3? Position = null);

/// <summary>An item on the ground and where it is on screen.</summary>
internal readonly record struct GroundSpot(long Id, Point Spot);

/// <summary>Everything the bot knows about one moment. The brain never sees pixels or memory.</summary>
/// <param name="Hp">The character's HP (0..1), from memory; null when it can't be read.</param>
/// <param name="TargetHp">The selected mob's HP (0..1; 0 = nothing selected), from memory; null when unknown.</param>
internal sealed record GameState(
    TimeSpan Time,
    int Width,
    int Height,
    double? Hp,
    double? Mp,
    double? TargetHp,
    double AnalysisMs)
{
    public bool HasTarget => TargetHp is > 0.01;

    /// <summary>The mobs on screen (client pixels, nearest to the character first), from memory.</summary>
    public IReadOnlyList<Point> MobCandidates { get; init; } = [];

    /// <summary>Mobs to go for before the others (attacking us, or Metin stones when preferred).</summary>
    public IReadOnlyList<Point> PriorityMobs { get; init; } = [];

    /// <summary>The mobs with their distance, reach and value (from memory); empty without memory.</summary>
    public IReadOnlyList<TargetInfo> Targets { get; init; } = [];

    /// <summary>The saved farm spot to walk to before starting, when it is used.</summary>
    public Vec3? FarmSpot { get; init; }

    /// <summary>Where the mobs were seen most (world), to walk to when none is on screen.</summary>
    public Vec3? Hotspot { get; init; }

    /// <summary>The character's attack value (to guess how long a mob takes to kill).</summary>
    public int AttackPower { get; init; }

    /// <summary>Mobs attacking the character (on screen).</summary>
    public IReadOnlyList<Point> Attackers { get; init; } = [];

    /// <summary>The character's skills on quick slot keys (from memory).</summary>
    public IReadOnlyList<SkillSlot> Skills { get; init; } = [];

    /// <summary>MP in points (for skill costs), when known.</summary>
    public int? MpPoints { get; init; }

    /// <summary>HP potions left in the inventory, when known.</summary>
    public int? HpPotionsLeft { get; init; }

    /// <summary>Items on the ground close to the character (on screen).</summary>
    public IReadOnlyList<GroundSpot> Drops { get; init; } = [];

    /// <summary>The character is moving (its position changed a moment ago) — null when not known.</summary>
    public bool? Walking { get; init; }

    /// <summary>The character's position in the world, from memory.</summary>
    public Vec3? Position { get; init; }

    /// <summary>The game camera (which way "screen up" points in the world).</summary>
    public CameraView? Camera { get; init; }

    /// <summary>The character is dead.</summary>
    public bool Dead { get; init; }

    /// <summary>"Burada yeniden başla" can be clicked (its text turned white).</summary>
    public bool ReviveReady { get; init; }

    /// <summary>A window covers the game (e.g. the advert after a level-up).</summary>
    public bool PopupOpen { get; init; }

    /// <summary>Where the selected mob is on screen (client pixels), if known.</summary>
    public Point? TargetSpot { get; init; }
}
