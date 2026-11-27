using SekoBot.Bot;
using SekoBot.UI;
using SekoBot.Update;
using Xunit;

namespace SekoBot.Tests;

public class UpdateRecoveryTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(200, true)]
    [InlineData(400, false)]
    public void Unchanged_or_unreadable_stock_blocks_another_purchase(int? observed, bool pending)
    {
        Assert.Equal(pending, BotEngine.StockStillPending(200, observed));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Interrupted_checks_report_a_reason(bool cancelled)
    {
        var result = UpdateService.Interrupted(cancelled);
        Assert.Null(result.Update);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Two_downloads_do_not_share_a_staging_file()
    {
        string target = Path.Combine(Path.GetTempPath(), "SekoBot.exe");
        string first = UpdateService.DownloadPath(target);
        Assert.Equal(Path.GetDirectoryName(target), Path.GetDirectoryName(first));
        Assert.NotEqual(first, UpdateService.DownloadPath(target));
    }

    [Fact]
    public void Failed_resume_retries_but_concurrent_and_completed_attempts_do_not_start_twice()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "resume.json");
        try
        {
            var now = DateTime.Now;
            SharedFile.Update<Dictionary<string, MainForm.WorkBeforeUpdate>>(path,
                notes => notes["Ali"] = new() { Running = true, Mode = 1, At = now });
            int starts = 0;
            MainForm.TryResume(path, "Ali", now, work =>
            {
                starts++;
                MainForm.TryResume(path, "Ali", now, _ => throw new InvalidOperationException("duplicate"));
                return false;
            });
            MainForm.TryResume(path, "Ali", now.AddSeconds(6), work => { starts++; return true; });
            MainForm.TryResume(path, "Ali", now.AddSeconds(12), work => { starts++; return true; });
            Assert.Equal(2, starts);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
