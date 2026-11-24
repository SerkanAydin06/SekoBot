using SekoBot.Config;

namespace SekoBot.Bot;

/// <summary>
/// Telegram answers for the other SekoBots: only the first SekoBot reads Telegram, but a whisper
/// to IMEDUSA06 must be answered from IMEDUSA06's game. The answer is put in that SekoBot's inbox
/// (gelen-yanit-N.json) and it writes it in its own game; who plays which character is known from
/// the status files every SekoBot writes (durum-N.txt, first line "👤 NAME …").
/// </summary>
internal static class WhisperInbox
{
    public sealed class Letter
    {
        public string To { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public static string Folder { get; set; } = ProfileStore.Directory;

    private static string InboxOf(int number) => Path.Combine(Folder, $"gelen-yanit-{number}.json");

    /// <summary>The character a Telegram notice was about: its first line "SekoBot · NAME".</summary>
    public static string? CharacterOf(string notice)
    {
        string first = notice.Split('\n', 2)[0].Trim();
        // "PARDUS · " now; "SekoBot · " on notices sent before the new name.
        foreach (string mark in new[] { "PARDUS · ", "SekoBot · " })
            if (first.StartsWith(mark, StringComparison.Ordinal) && first.Length > mark.Length)
                return first[mark.Length..].Trim();
        return null;
    }

    /// <summary>The SekoBot (its number) playing a character now, from the status files written within the last minute.</summary>
    public static int? SekoBotOf(string character)
    {
        for (int n = 1; n <= 6; n++)
        {
            try
            {
                var file = new FileInfo(Path.Combine(Folder, $"durum-{n}.txt"));
                if (!file.Exists || DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromMinutes(1))
                    continue;
                string first = File.ReadLines(file.FullName).FirstOrDefault() ?? "";
                // "👤 IMEDUSA06 (2. SekoBot)" or "👤 KAiZER06"
                string name = first.Replace("👤", "").Split('(')[0].Trim();
                if (name.Equals(character, StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>Puts an answer in another SekoBot's inbox.</summary>
    public static bool Send(int number, string to, string text) =>
        SharedFile.Update<List<Letter>>(InboxOf(number), letters => letters.Add(new Letter { To = to, Text = text })) != null;

    /// <summary>Takes what is waiting in this SekoBot's inbox (empty when nothing).</summary>
    public static List<Letter> Take(int number)
    {
        string path = InboxOf(number);
        if (!File.Exists(path) || new FileInfo(path).Length <= 2)
            return [];
        var taken = new List<Letter>();
        SharedFile.Update<List<Letter>>(path, letters =>
        {
            taken.AddRange(letters);
            letters.Clear();
        });
        return taken;
    }
}
