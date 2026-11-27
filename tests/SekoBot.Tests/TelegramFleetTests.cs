using SekoBot.Update;
using Xunit;

namespace SekoBot.Tests;

public class TelegramFleetTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void Commands_only_reach_the_selected_session_once()
    {
        var hub = new TelegramFleet(_folder, "token", "chat");
        var worker = new TelegramFleet(_folder, "token", "chat");
        Guid id = Guid.NewGuid();
        worker.Announce(new(id, "PC", 2, "Ali", DateTimeOffset.UtcNow));
        Assert.Equal(id, Assert.Single(hub.Online()).Id);
        Assert.Empty(new TelegramFleet(_folder, "token", "other chat").Online());
        hub.Send(new(id, "Ali", "ekran", 10, DateTimeOffset.UtcNow));
        Assert.Empty(worker.Take(Guid.NewGuid()));
        Assert.Equal("ekran", Assert.Single(worker.Take(id)).Action);
        Assert.Empty(worker.Take(id));
    }

    [Fact]
    public void Expired_and_modified_requests_are_discarded()
    {
        var fleet = new TelegramFleet(_folder, "token", "chat");
        Guid id = Guid.NewGuid();
        fleet.Send(new(id, "Ali", "ekran", 1, DateTimeOffset.UtcNow.AddMinutes(-5)));
        fleet.Send(new(id, "Ali", "ekran", 2, DateTimeOffset.UtcNow));
        var file = Directory.GetFiles(_folder, "*2.cmd", SearchOption.AllDirectories).Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("ekran", "basla"));
        Assert.Empty(fleet.Take(id));
    }
}
