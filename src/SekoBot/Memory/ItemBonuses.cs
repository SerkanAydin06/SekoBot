namespace SekoBot.Memory;

/// <summary>A bonus ("efsun") of an item: its kind (the game's number) and value (e.g. 10 = +10 %).</summary>
internal readonly record struct ItemBonus(int Type, int Value);

/// <summary>
/// The names of the bonus kinds. The game lists them in gameManager.efsunlar (index = kind:
/// "APPLY_APPLY_ATTBONUS_ORC" is 35 — checked in the game: an item with 35 / 10 shows "Orklara karşı
/// güçlü +%10", 41 / 3 "Zehirleme şansı %3"); the list seen then is built in, read again from the game.
/// </summary>
internal static class ItemBonuses
{
    private static volatile string[] _codes =
    [
        "", "MOV_SPEED", "ATT_GRADE", "RESIST_MAGIC", "RESIST_FIRE", "DEX", "DEF_GRADE_BONUS", "STR", "INT", "CON",
        "MAX_HP", "MAX_SP", "SKILL_DAMAGE_BONUS", "DEFEND_BONUS", "NORMAL_HIT_DAMAGE_BONUS", "HIT_DEFEND_BONUS",
        "CRITICAL_PCT", "PENETRATE_PCT", "RESIST_DAGGER", "RESIST_SWORD", "RESIST_TWOHAND", "RESIST_ASSASSIN",
        "RESIST_SURA", "RESIST_WARRIOR", "ATTBONUS_ASSASSIN", "ATTBONUS_SURA", "ATTBONUS_WARRIOR", "KILL_HP_RECOVER",
        "KILL_SP_RECOVER", "CAST_SPEED", "ATTBONUS_ANIMAL", "ATTBONUS_DEVIL", "ATTBONUS_HUMAN", "ATTBONUS_MILGYO",
        "ATTBONUS_MONSTER", "ATTBONUS_ORC", "ATTBONUS_UNDEAD", "BLEEDING_PCT", "BLEEDING_REDUCE", "BLOCK",
        "REFLECT_MELEE", "POISON_PCT", "POISON_REDUCE", "IMMUNE_FALL", "IMMUNE_SLOW", "IMMUNE_STUN", "ITEM_DROP_BONUS",
        "EXP_DOUBLE_BONUS", "GOLD_DOUBLE_BONUS", "ATT_SPEED", "RESIST_FAN", "RESIST_BOW", "SLOW_PCT", "STUN_PCT",
        "RESIST_BELL", "DODGE", "BLOCK", "HP_REGEN", "SP_REGEN", "RESIST_WIND", "RESIST_ELEC", "RESIST_WARRIOR",
        "ATTBONUS_ASSASSIN",
    ];

    private static readonly Dictionary<string, string> Turkish = new()
    {
        ["MOV_SPEED"] = "Hareket hızı", ["ATT_GRADE"] = "Saldırı değeri", ["RESIST_MAGIC"] = "Büyüye karşı dayanıklılık",
        ["RESIST_FIRE"] = "Ateşe karşı dayanıklılık", ["DEX"] = "Çeviklik", ["DEF_GRADE_BONUS"] = "Savunma",
        ["STR"] = "Güç", ["INT"] = "Zeka", ["CON"] = "Canlılık", ["MAX_HP"] = "Max. HP", ["MAX_SP"] = "Max. SP",
        ["SKILL_DAMAGE_BONUS"] = "Beceri hasarı", ["DEFEND_BONUS"] = "Beceri hasarına karşı dayanıklılık",
        ["NORMAL_HIT_DAMAGE_BONUS"] = "Ortalama zarar", ["HIT_DEFEND_BONUS"] = "Ortalama zarara karşı dayanıklılık",
        ["CRITICAL_PCT"] = "Kritik vuruş şansı", ["PENETRATE_PCT"] = "Delici vuruş şansı",
        ["RESIST_DAGGER"] = "Hançer savunması", ["RESIST_SWORD"] = "Kılıç savunması", ["RESIST_TWOHAND"] = "Çift el savunması",
        ["RESIST_ASSASSIN"] = "Ninjaya karşı dayanıklılık", ["RESIST_SURA"] = "Suraya karşı dayanıklılık",
        ["RESIST_WARRIOR"] = "Savaşçıya karşı dayanıklılık", ["ATTBONUS_ASSASSIN"] = "Ninjalara karşı güçlü",
        ["ATTBONUS_SURA"] = "Suralara karşı güçlü", ["ATTBONUS_WARRIOR"] = "Savaşçılara karşı güçlü",
        ["KILL_HP_RECOVER"] = "Öldürünce HP yenileme", ["KILL_SP_RECOVER"] = "Öldürünce SP yenileme",
        ["CAST_SPEED"] = "Büyü hızı", ["ATTBONUS_ANIMAL"] = "Hayvanlara karşı güçlü", ["ATTBONUS_DEVIL"] = "Şeytanlara karşı güçlü",
        ["ATTBONUS_HUMAN"] = "Yarı insanlara karşı güçlü", ["ATTBONUS_MILGYO"] = "Mistiklere karşı güçlü",
        ["ATTBONUS_MONSTER"] = "Canavarlara karşı güçlü", ["ATTBONUS_ORC"] = "Orklara karşı güçlü",
        ["ATTBONUS_UNDEAD"] = "Ölümsüzlere karşı güçlü", ["BLEEDING_PCT"] = "Kanama şansı",
        ["BLEEDING_REDUCE"] = "Kanamaya karşı dayanıklılık", ["BLOCK"] = "Bedensel saldırı bloklama",
        ["REFLECT_MELEE"] = "Yakın dövüş yansıtma", ["POISON_PCT"] = "Zehirleme şansı", ["POISON_REDUCE"] = "Zehre karşı dayanıklılık",
        ["IMMUNE_FALL"] = "Düşmeye karşı bağışıklık", ["IMMUNE_SLOW"] = "Yavaşlamaya karşı bağışıklık",
        ["IMMUNE_STUN"] = "Sersemlemeye karşı bağışıklık", ["ITEM_DROP_BONUS"] = "Eşya düşme şansı",
        ["EXP_DOUBLE_BONUS"] = "EXP bonusu", ["GOLD_DOUBLE_BONUS"] = "Yang düşme şansı", ["ATT_SPEED"] = "Saldırı hızı",
        ["RESIST_FAN"] = "Yelpaze savunması", ["RESIST_BOW"] = "Ok savunması", ["SLOW_PCT"] = "Yavaşlatma şansı",
        ["STUN_PCT"] = "Sersemletme şansı", ["RESIST_BELL"] = "Çan savunması", ["DODGE"] = "Oklardan kaçınma",
        ["HP_REGEN"] = "HP yenilenmesi", ["SP_REGEN"] = "SP yenilenmesi", ["RESIST_WIND"] = "Rüzgara karşı dayanıklılık",
        ["RESIST_ELEC"] = "Şimşeğe karşı dayanıklılık",
    };

    /// <summary>The game's list as read ("APPLY_APPLY_ATTBONUS_ORC"…), when it has names.</summary>
    public static void Learn(IReadOnlyList<string> names)
    {
        if (names.Count < 2 || names.Count(n => n.Length > 0) < 10)
            return;
        _codes = [.. names.Select(Code)];
    }

    private static string Code(string name)
    {
        string code = name.Trim();
        while (code.StartsWith("APPLY_", StringComparison.Ordinal))
            code = code[6..];
        return code;
    }

    /// <summary>The bonus kind's name in Turkish (the game's code when not known; empty for none).</summary>
    public static string NameOf(int type)
    {
        var codes = _codes;
        string code = type > 0 && type < codes.Length ? codes[type] : "";
        return code.Length == 0 ? (type > 0 ? $"efsun #{type}" : "") : Turkish.GetValueOrDefault(code, code);
    }

    /// <summary>Every kind with a name, one per name (the game lists a few twice): kind number, name.</summary>
    public static IReadOnlyList<(int Type, string Name)> All()
    {
        var codes = _codes;
        var seen = new HashSet<string>();
        var all = new List<(int, string)>();
        for (int i = 1; i < codes.Length; i++)
            if (codes[i].Length > 0 && seen.Add(codes[i]))
                all.Add((i, NameOf(i)));
        return all;
    }

    /// <summary>Kinds meaning the same as this one (the game lists a few twice, e.g. 39 and 56 "BLOCK").</summary>
    public static bool Same(int a, int b)
    {
        var codes = _codes;
        return a == b || a > 0 && b > 0 && a < codes.Length && b < codes.Length && codes[a].Length > 0 && codes[a] == codes[b];
    }

    /// <summary>"Orklara karşı güçlü +10" for a log line.</summary>
    public static string Describe(IEnumerable<ItemBonus> bonuses) =>
        string.Join(", ", bonuses.Where(b => b.Type > 0).Select(b => $"{NameOf(b.Type)} +{b.Value}"));
}
