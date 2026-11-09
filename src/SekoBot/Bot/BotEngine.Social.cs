using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// While the bot runs: private messages, requests, the game's questions, our name or a GM in the
/// chat, drop events and unspent points go to the log and Telegram (see <see cref="SocialWatch"/>);
/// urgent ones also with a sound. The items of the drop events going on now are picked up.
/// </summary>
internal sealed partial class BotEngine
{
    private readonly SocialWatch _social = new();
    private TimeSpan _socialReadAt = TimeSpan.FromDays(-1);

    private void SocialTick(WorldSnapshot world, BotProfile profile, TimeSpan now)
    {
        if (now - _socialReadAt < TimeSpan.FromSeconds(1) || _world?.ReadSocial() is not { } social)
            return;
        _socialReadAt = now;
        List<SocialNotice> notices;
        lock (_social)
        {
            notices = _social.Update(social, profile, world.Level, DateTime.Now);
            Loot.EventItems = profile.EventLoot ? [.. _social.ActiveEvents.Select(e => e.Item)] : [];
        }
        foreach (var notice in notices)
        {
            _log(notice.Text);
            Notify(notice.Text);
            if (notice.Urgent)
                Alert();
        }
    }

    /// <summary>The drop events for the window: the ones going on now first, then the next ones.</summary>
    public string EventsSummary()
    {
        lock (_social)
        {
            var now = DateTime.Now;
            var active = _social.ActiveEvents;
            var next = _social.Events.Where(e => e.Start > now).OrderBy(e => e.Start).Take(2).ToList();
            if (active.Count == 0 && next.Count == 0)
                return _social.Events.Count == 0 ? "Bot çalışınca görünür." : "Şu an etkinlik yok.";
            var lines = active.Select(e => $"▶ {ItemBook.NameOf(e.Item)} — {e.End:HH:mm}'e kadar")
                .Concat(next.Select(e => $"{e.Start:HH:mm} {ItemBook.NameOf(e.Item)}" + (e.MinLevel > 1 ? $" (Sv {e.MinLevel}+)" : "")));
            return string.Join("\n", lines);
        }
    }
}
