using System.Text;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Mobs from memory: every living, attackable mob of the game world (see <see cref="GameWorldReader"/>),
/// put on the screen with the game camera, so the bot sees every mob, also those without a name shown.
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>What the bot sees on screen from memory: the mobs it may go for (nearest to us
    /// first), the ones to go for first, and the items close by. Null when the camera can't be read.</summary>
    internal static (List<Point> Mobs, List<Point> Priority, List<GroundSpot> Drops, List<Point> Attackers, List<TargetInfo> Targets)? MemoryView(
        WorldSnapshot? world, BotProfile profile, int width, int height)
    {
        if (world?.Camera is not CameraView camera)
            return null;
        var mobs = new List<Point>();
        var attackers = new List<Point>();
        var metins = new List<Point>();
        var found = new List<(WorldMob Mob, Point Spot)>();
        foreach (var mob in world.ByDistance)
        {
            if (!Wanted(mob, profile))
                continue;
            var onScreen = camera.ToScreen(mob.Position, width, height) is PointF p && OnPlayfield(p, width, height) ? p : (PointF?)null;
            if (onScreen is PointF seen)
            {
                var spot = Point.Round(seen);
                mobs.Add(spot);
                if (mob.AttacksMe)
                    attackers.Add(spot);
                if (mob.IsMetin && profile.Metins == MetinMode.Prefer)
                    metins.Add(spot);
                found.Add((mob, spot));
            }
            else if (mob.Position.DistanceXY(world.Player) <= OffScreenRange)
            {
                // Known from memory but off the screen (the camera shows only ~10 m around):
                // still a target, walked to in its direction.
                found.Add((mob, OffScreenSpot(camera, world.Player, mob.Position, width, height)));
            }
        }
        var drops = new List<GroundSpot>();
        if (profile.PickUpDrops)
            foreach (var item in (world.Drops ?? []).Where(d => WantedLoot(d, profile)).OrderBy(d => d.Position.DistanceXY(world.Player)))
                if (item.Position.DistanceXY(world.Player) <= profile.DropRangeMetres * 100 &&
                    camera.ToScreen(item.Position, width, height) is PointF p && OnPlayfield(p, width, height))
                    drops.Add(new GroundSpot(item.Id, Point.Round(p)));
        // Metin stones set to "first" come before everything, also before mobs attacking us
        // (those stand around the stone and get hit too).
        var priority = metins.Count > 0 ? metins : profile.AttackersFirst ? attackers : [];
        // The character hits from AttackDistance between the centres (measured in the game: hits at
        // 70–145 with a reach of 130); a bigger body (a Metin stone) can be hit from further away.
        double attackDistance = world.AttackDistance > 0 ? world.AttackDistance : 130;
        bool preferMetins = profile.Metins == MetinMode.Prefer && found.Any(f => f.Mob.IsMetin);
        var groups = SpawnGroups(found.Select(f => f.Mob).ToList());
        var targets = found.Select(f => new TargetInfo(f.Mob.Id, f.Spot, f.Mob.Position.DistanceXY(world.Player),
                attackDistance + Math.Max(0, f.Mob.Radius - 40), f.Mob.Exp, f.Mob.Hp, f.Mob.MaxHp, f.Mob.DamageMax,
                preferMetins ? f.Mob.IsMetin : profile.AttackersFirst && f.Mob.AttacksMe,
                f.Mob.AttacksMe, groups.GetValueOrDefault(f.Mob.Id), f.Mob.Position))
            .ToList();
        return (mobs, priority, drops, attackers, targets);
    }

    /// <summary>
    /// The spawn groups (the game spawns mobs in threes; hitting one brings the others). Seen in the
    /// game: a group's mobs get numbers next to each other and stand together, and the numbers are
    /// handed out in threes that all start at the same place (1877–79, 1880–82, 1883–85, … — the
    /// lowest of each three leaves the same remainder divided by 3). Two threes standing side by side
    /// (1886–88 next to 1889–91) are two groups, not one. So: mobs in the same three of numbers that
    /// stand within 10 m are one group, named by the lowest number; Metin stones have none. Where the
    /// threes start is worked out from the mobs themselves (the start that keeps most neighbours together).
    /// </summary>
    internal static Dictionary<int, int> SpawnGroups(IReadOnlyList<WorldMob> mobs)
    {
        var list = mobs.Where(m => !m.IsMetin).OrderBy(m => m.Id).ToList();
        // Neighbours: numbers at most 2 apart, standing within 10 m.
        var pairs = new List<(WorldMob A, WorldMob B)>();
        for (int i = 0; i < list.Count; i++)
        for (int j = i + 1; j < list.Count && list[j].Id - list[i].Id <= 2; j++)
            if (list[i].Position.DistanceXY(list[j].Position) <= 1000)
                pairs.Add((list[i], list[j]));
        static int Three(int id, int start) => (int)Math.Floor((id - start) / 3.0);
        int start = Enumerable.Range(0, 3).MaxBy(r => pairs.Sum(pair => Three(pair.A.Id, r) == Three(pair.B.Id, r) ? 1 : -1));

        var parent = list.ToDictionary(m => m.Id, m => m.Id);
        int Root(int id)
        {
            while (parent[id] != id)
                id = parent[id] = parent[parent[id]];
            return id;
        }
        foreach (var (a, b) in pairs)
            if (Three(a.Id, start) == Three(b.Id, start))
            {
                int ra = Root(a.Id), rb = Root(b.Id);
                if (ra != rb)
                    parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }
        return parent.Keys.ToDictionary(id => id, Root);
    }

    /// <summary>Mobs this close (world units) are targets even when they're off the screen.</summary>
    internal const double OffScreenRange = 2500;

    /// <summary>A point in the direction of an off-screen mob (from the screen centre), for walking to it.</summary>
    private static Point OffScreenSpot(CameraView camera, Vec3 player, Vec3 mob, int width, int height)
    {
        var (right, up) = camera.ToScreenDirection(mob.X - player.X, mob.Y - player.Y);
        double length = Math.Max(1e-6, Math.Sqrt(right * right + up * up));
        double reach = Math.Max(width, height); // well outside the screen
        return new Point((int)(width / 2.0 + right / length * reach), (int)(height / 2.0 - up / length * reach));
    }

    /// <summary>
    /// The character's skills that sit on a quick slot, with the key that uses them. Quick slots
    /// 1–5 are the number keys 1–5, slots 6–10 the skill keys (F1–F5); a skill on a slot is stored
    /// as −(100 + skill number).
    /// </summary>
    internal static List<SkillSlot> SkillKeys(WorldSnapshot? world, BotProfile profile)
    {
        var slots = new List<SkillSlot>();
        if (world?.Progress is not { } progress || world.Skills is not { Count: > 0 } skills)
            return slots;
        for (int i = 0; i < progress.QuickSlots.Count && i < 10; i++)
        {
            if (progress.QuickSlots[i] is not (< -100 and var value) || skills.FirstOrDefault(s => s.Id == -value - 100) is not { } skill)
                continue;
            Keys? key = i < 5 ? Keys.D1 + i : i - 5 < profile.Keys.Skills.Length ? profile.Keys.Skills[i - 5] : null;
            if (key is Keys k)
                slots.Add(new SkillSlot(k, skill.Name, skill.Kind, skill.Mana, skill.CooldownSec, skill.DurationSec));
        }
        return slots;
    }

    /// <summary>Whether to walk to this item (unknown items: yes, they may be anything).</summary>
    internal static bool WantedLoot(GroundItem item, BotProfile profile) =>
        !profile.LootSkip.Contains(item.Vnum) && (AlwaysLoot(item.Vnum, profile) || item.Kind switch
    {
        LootKind.Yang => profile.LootYang,
        LootKind.Potion => profile.LootPotions,
        LootKind.Equipment => profile.LootEquipment ||
                              profile.LootEquipmentMinGold > 0 && ItemBook.Get(item.Vnum)?.Gold >= profile.LootEquipmentMinGold,
        LootKind.Other => profile.LootOther,
        _ => true,
    });

    /// <summary>An item ticked in the item list, or on the "always pick up" names (part of a name is enough).</summary>
    internal static bool AlwaysLoot(int vnum, BotProfile profile)
    {
        if (vnum > 1 && profile.LootPick.Contains(vnum))
            return true;
        if (profile.LootAlways.Length == 0 || vnum <= 1 || ItemBook.Get(vnum) is not { } info)
            return false;
        return profile.LootAlways.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(n => info.Name.Contains(n, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Whether the bot should go for this mob, by the profile's choices. A mob that is
    /// attacking us is always fought back, and a Metin stone set to "first" is always gone for
    /// (the level and name filters are for mobs).</summary>
    internal static bool Wanted(WorldMob mob, BotProfile profile)
    {
        if (mob.IsMetin)
            return profile.Metins != MetinMode.Skip &&
                   (profile.Metins == MetinMode.Prefer || WantedByFilters(mob, profile));
        if (mob.AttacksMe)
            return true;
        return WantedByFilters(mob, profile);
    }

    private static bool WantedByFilters(WorldMob mob, BotProfile profile)
    {
        if (mob.AttacksMe)
            return true;
        if (profile.MaxMobLevel > 0 && mob.Level > profile.MaxMobLevel)
            return false;
        if (Names(profile.SkipMobs).Any(n => mob.Name.Contains(n, StringComparison.CurrentCultureIgnoreCase)))
            return false;
        var only = Names(profile.OnlyMobs);
        if (only.Count > 0 && !only.Any(n => mob.Name.Contains(n, StringComparison.CurrentCultureIgnoreCase)))
            return false;
        return true;
    }

    private static List<string> Names(string list) =>
        list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Where the selected mob is on screen, from memory (null when unknown or off screen).</summary>
    private static Point? MemoryTargetSpot(WorldSnapshot? world, int width, int height) =>
        world is { Target: { Hp: > 0 } target, Camera: { } camera } &&
        camera.ToScreen(target.Position, width, height) is PointF p && OnPlayfield(p, width, height)
            ? Point.Round(p)
            : null;

    /// <summary>On screen and not under the game's buttons (a click there would hit the button).</summary>
    internal static bool OnPlayfield(PointF p, int width, int height)
    {
        double x = p.X / width, y = p.Y / height;
        if (x < 0.02 || x > 0.98 || y < 0.04 || y > 0.9)
            return false;
        if (x > 0.86 && y < 0.36) // minimap, server name, clock
            return false;
        if (x < 0.11 && y > 0.18 && y < 0.78) // quest, message, event buttons on the left
            return false;
        return true;
    }

    /// <summary>"Mobları göster": the mobs around, read from memory, as text for the log.</summary>
    public string DescribeMobs()
    {
        if (World is not { } world)
            return "Mob listesi okunamadı: " + _memoryStatus + ". SekoBot'u yönetici olarak açtığından emin ol.";
        var mobs = world.ByDistance.ToList();
        var text = new StringBuilder();
        text.Append($"Bellekten: {mobs.Count} mob, {world.OtherPlayers} başka oyuncu (harita {world.MapName}, " +
                    $"konum X={world.Player.X:0}, Y={world.Player.Y:0}).");
        if (world.Target is { } target)
            text.Append($" Seçili hedef: {target.Name} (Sv {target.Level}) HP {target.Hp}/{target.MaxHp}.");
        if (world.Stats is { } stats)
            text.Append($" Karakter: Sv {world.Level}, HP {stats.Hp}/{stats.MaxHp}, MP {stats.Mp}/{stats.MaxMp}{(world.Dead ? ", ÖLÜ" : "")}.");
        if (world.Camera == null)
            text.Append(" Kamera okunamadı: moblar ekrana yerleştirilemiyor.");
        foreach (var mob in mobs.Take(12))
            text.Append($"\n  • {mob.Name} (Sv {mob.Level})  HP {mob.Hp}/{mob.MaxHp}  {mob.Position.DistanceXY(world.Player) / 100:0.0} m");
        if (mobs.Count > 12)
            text.Append($"\n  … ve {mobs.Count - 12} mob daha");
        return text.ToString();
    }
}
