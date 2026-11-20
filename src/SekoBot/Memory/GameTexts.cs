namespace SekoBot.Memory;

/// <summary>
/// The game's own messages (from "SYSTEM", e.g. the congratulation after a level up, seen 2026-11-20) come as
/// text numbers with their values: "100516||||100517||21||1" = text 100516 ("Seviye Atladın!") with no value,
/// then text 100517 ("Tebrikler! Karakterin %s. seviyeye ulaştı.") with "21". The texts are the game's
/// (m2data.Locale, number → text); this puts the message together as the game shows it.
/// </summary>
internal static class GameTexts
{
    /// <summary>Text numbers are this big at least (a smaller number is a value, e.g. a level).</summary>
    internal const int FirstText = 1000;

    /// <summary>The message as words; <paramref name="raw"/> itself when it isn't one of these.</summary>
    public static string Decode(string raw, Func<int, string?> text)
    {
        if (!raw.Contains("||", StringComparison.Ordinal) || !char.IsDigit(raw.FirstOrDefault()))
            return raw;
        var parts = raw.Split("||");
        var said = new List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int id) || id < FirstText || text(id) is not { Length: > 0 } template)
                continue;
            // Its values: the next parts that aren't empty, one for each %s.
            int wanted = Count(template, "%s") + Count(template, "%d");
            var values = new List<string>();
            while (values.Count < wanted && i + 1 < parts.Length)
            {
                i++;
                if (parts[i].Length > 0)
                    values.Add(parts[i]);
            }
            string line = template;
            foreach (string value in values)
            {
                int at = IndexOfPlaceholder(line);
                if (at < 0)
                    break;
                line = line[..at] + value + line[(at + 2)..];
            }
            said.Add(line.Trim());
        }
        return said.Count > 0 ? string.Join(" ", said) : raw;
    }

    private static int Count(string s, string what)
    {
        int n = 0;
        for (int at = s.IndexOf(what, StringComparison.Ordinal); at >= 0; at = s.IndexOf(what, at + what.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static int IndexOfPlaceholder(string s)
    {
        int a = s.IndexOf("%s", StringComparison.Ordinal), b = s.IndexOf("%d", StringComparison.Ordinal);
        return a < 0 ? b : b < 0 ? a : Math.Min(a, b);
    }
}
