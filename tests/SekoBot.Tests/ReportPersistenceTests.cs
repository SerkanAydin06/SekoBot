using System.Text.Json;
using SekoBot.Bot;
using Xunit;

namespace SekoBot.Tests;

public class ReportPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SekoBotTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Failed_replace_preserves_previous_json()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "shared.json");
        File.WriteAllText(path, "{\"a\":1}");
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Null(SharedFile.Update<Dictionary<string, int>>(path, map => map["a"] = 2));
            Assert.Equal("{\"a\":1}", File.ReadAllText(path));
        }
        Assert.True(SharedFile.Update<Dictionary<string, int>>(path, map => map["a"]++)?["a"] == 2);
    }

    [Fact]
    public void Concurrent_updates_preserve_all_changes()
    {
        string path = Path.Combine(_dir, "shared.json");
        Parallel.For(0, 8, i => Assert.NotNull(SharedFile.Update<Dictionary<string, int>>(path, map => map[i.ToString()] = i)));
        Assert.Equal(8, JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path))!.Count);
    }

    [Fact]
    public void Failed_item_report_can_retry_without_another_item_event()
    {
        string path = Path.Combine(_dir, "items.txt");
        Directory.CreateDirectory(path);
        var report = new SessionItems(path);
        report.Sold(123, 17);
        report.Save(force: true);
        Directory.Delete(path);
        report.Save(force: true);
        Assert.Contains("17", File.ReadAllLines(path)[^1]);
    }
}
