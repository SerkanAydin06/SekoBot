using SekoBot.Bot;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Fishing: the pulls grouped by the wait before them, to find the best wait.</summary>
public class FishPullsTests
{
    [Fact]
    public void Pulls_are_grouped_by_their_wait()
    {
        var bins = FishPulls.Bins([(600, true), (700, false), (1500, true), (1550, true), (1600, false), (2600, false)]);
        Assert.Equal(FishPulls.Edges.Length + 1, bins.Count);
        Assert.Equal((2, 1), (bins[0].Pulls, bins[0].Caught));
        Assert.Equal("< 0.8 sn".Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator), bins[0].Name);
        var mid = bins.Single(b => b.From <= 1.5 && b.To > 1.5);
        Assert.Equal((3, 2), (mid.Pulls, mid.Caught));
        Assert.Equal((1, 0), (bins[^1].Pulls, bins[^1].Caught));
        Assert.Equal(6, bins.Sum(b => b.Pulls));
    }

    [Fact]
    public void The_file_is_read_without_its_header_and_waitless_lines()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "Zaman;Bekleme (ms);Sonuç;Eşya;Karakter\n" +
                                    "2026-11-19 17:35:52;1616;yakalandı;Ot Sazanı;A\n" +
                                    "2026-11-19 17:36:11;1495;kaçtı;;A\n" +
                                    "2026-11-19 17:37:00;;resim tutmadı;;A\n");
            var pulls = FishPulls.Read(file);
            Assert.Equal([(1616, true), (1495, false)], pulls);
        }
        finally { File.Delete(file); }
    }
}
