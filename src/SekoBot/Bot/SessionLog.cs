using System.Globalization;
using System.Text;
using SekoBot.Config;

namespace SekoBot.Bot;

/// <summary>
/// The summary of every bot run, one line per run in %LOCALAPPDATA%\SekoBot\oturumlar.csv
/// (semicolon separated, opens in Excel), and as text for the log and Telegram.
/// </summary>
internal static class SessionLog
{
    public static string FilePath => Path.Combine(ProfileStore.Directory, "oturumlar.csv");

    private const string Header =
        "Başlangıç;Bitiş;Süre (dk);Harita;Öldürülen;Saatte mob;EXP;Saatte EXP;Seviye;Atlanan seviye;Yang;HP iksiri;MP iksiri;Ölüm;Durma sebebi";

    /// <summary>One line for the file.</summary>
    internal static string Line(DateTime start, DateTime end, string map, SessionReport r, int deaths, string reason)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        double minutes = Math.Max(0, (end - start).TotalMinutes), hours = minutes / 60;
        string PerHour(double value) => hours > 0 ? (value / hours).ToString("0", tr) : "0";
        string Clean(string text) => text.Replace(';', ',').Replace('\n', ' ');
        return string.Join(';',
            start.ToString("yyyy-MM-dd HH:mm", tr), end.ToString("yyyy-MM-dd HH:mm", tr), minutes.ToString("0", tr), Clean(map),
            r.Kills, PerHour(r.Kills), r.Exp, PerHour(r.Exp), r.Level, r.LevelsGained, r.Yang,
            r.HpPotionsUsed, r.MpPotionsUsed, deaths, Clean(reason));
    }

    /// <summary>A short text: "42 dk · 310 mob (443/sa) · 12.345 EXP (17.636/sa) · …".</summary>
    internal static string Summary(TimeSpan duration, SessionReport r, int deaths)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        double hours = Math.Max(duration.TotalHours, 1e-9);
        var text = new StringBuilder();
        text.Append($"{duration.TotalMinutes:0} dk · {r.Kills.ToString("N0", tr)} mob ({(r.Kills / hours).ToString("N0", tr)}/sa)");
        text.Append($" · {r.Exp.ToString("N0", tr)} EXP ({(r.Exp / hours).ToString("N0", tr)}/sa)");
        if (r.LevelsGained > 0)
            text.Append($" · +{r.LevelsGained} seviye (Sv {r.Level})");
        text.Append($" · {r.Yang.ToString("N0", tr)} yang · {r.HpPotionsUsed + r.MpPotionsUsed} iksir · {deaths} ölüm");
        return text.ToString();
    }

    public static void Append(string line)
    {
        Directory.CreateDirectory(ProfileStore.Directory);
        bool fresh = !File.Exists(FilePath);
        // UTF-8 with BOM so Excel shows the Turkish letters.
        using var writer = new StreamWriter(FilePath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        if (fresh)
            writer.WriteLine(Header);
        writer.WriteLine(line);
    }
}
