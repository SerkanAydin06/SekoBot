using System.Text.Json;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;

namespace SekoBot.Bot;

/// <summary>
/// Our own bots farming in the same place: each SekoBot tells the others where it is (map, channel, place;
/// bot-konum-N.json, every few seconds). Two or more of ours on the same map and channel close together for a
/// while take each other's mobs: the one with the lowest number stays, the others change channel — each to a
/// channel none of ours is on there (the user: "3 botumuz denk geldi aynı alana biri kalacak, diğer 2'si
/// farklı kanallara geçecek").
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>Ours this close (world units, 40 m) on the same map and channel are in the same place.</summary>
    internal const double SamePlace = 4000;
    /// <summary>In the same place this long (not just passing by) before one moves.</summary>
    internal static readonly TimeSpan CrowdFor = TimeSpan.FromSeconds(20);
    /// <summary>After changing channel (for any reason), this long before moving again.</summary>
    internal static readonly TimeSpan CrowdCooldown = TimeSpan.FromMinutes(5);
    /// <summary>A place file older than this is a SekoBot no longer running.</summary>
    internal static readonly TimeSpan PlaceFresh = TimeSpan.FromSeconds(20);

    /// <summary>Where one of our bots is (what each SekoBot writes for the others).</summary>
    internal sealed record BotPlace(int Number, string Character, string Map, int Channel, double X, double Y, bool Farming, DateTime At, string Server = "");

    private DateTime _placeWrittenAt = DateTime.MinValue, _channelReadAt = DateTime.MinValue, _crowdSince = DateTime.MinValue;
    private DateTime _lastChannelChange = DateTime.MinValue;
    private int _myChannel;
    /// <summary>Channels to keep off when changing (ours are on them here).</summary>
    private readonly HashSet<int> _channelAvoid = [];

    private volatile bool _channelAsked;

    /// <summary>The user asked for another channel (the mini bar's menu): done on the bot's next calm moment.</summary>
    public string ChangeChannelNow()
    {
        if (!BotRunning)
            return "🔀 Kanal değiştirmek için bot çalışıyor olmalı.";
        _channelAsked = true;
        return "🔀 Kanal değiştirilecek (botlarımızın olmadığı en boş kanala).";
    }

    internal static string PlaceFile(int number) => Path.Combine(ProfileStore.Directory, $"bot-konum-{number}.json");

    /// <summary>
    /// Who of a crowd of ours (same map, channel, close together) moves: everyone but the lowest number. True when
    /// <paramref name="me"/> should move.
    /// </summary>
    internal static bool ShouldMove(BotPlace me, IEnumerable<BotPlace> others) =>
        others.Any(o => o.Number < me.Number && o.Farming && o.Server == me.Server && o.Map == me.Map && o.Channel == me.Channel && o.Channel > 0
                        && Math.Sqrt((o.X - me.X) * (o.X - me.X) + (o.Y - me.Y) * (o.Y - me.Y)) <= SamePlace);

    /// <summary>Inside <see cref="_botGate"/>: the place shared, a crowd of ours noticed; the actions when this bot moves, else null.</summary>
    private IReadOnlyList<BotAction>? CrowdTick(GameState state, BotProfile p)
    {
        // A channel change under way (also when the PvP guard is off) goes on.
        if (_channelSince != null)
            return ChannelTick(state, p, DateTime.Now);
        if (World is not { } world || world.MapName.Length == 0 || IsLoginScreen(world.MapName))
            return null;
        var now = DateTime.Now;
        if (_channelAsked && !state.Dead && state.Attackers.Count == 0 && !_selling && !_cleaner.Active)
        {
            _channelAsked = false;
            _channelAvoid.Clear();
            _log("🔀 İstenildiği için kanal değiştiriliyor.");
            return StartChannelChange(state, p, now);
        }
        if (now - _channelReadAt > TimeSpan.FromSeconds(30) || _myChannel == 0)
        {
            _channelReadAt = now;
            lock (_worldGate)
                _myChannel = _world?.ReadChannels()?.Current ?? _myChannel;
        }
        if (now - _placeWrittenAt < TimeSpan.FromSeconds(5))
            return null;
        _placeWrittenAt = now;
        var me = new BotPlace(SekoBot.Instance.Number, CharacterName, world.MapName, _myChannel, world.Player.X, world.Player.Y, p.Attack, now.ToUniversalTime(), world.Server);
        WritePlace(me);
        if (!p.CrowdGuard || !p.Attack || _myChannel == 0)
            return null;
        var others = ReadPlaces(me.Number);
        bool move = ShouldMove(me, others);
        if (!move)
        {
            _crowdSince = DateTime.MinValue;
            return null;
        }
        if (_crowdSince == DateTime.MinValue)
            _crowdSince = now;
        // Staggered by number: the earlier mover's new channel shows in its file before the next one picks.
        var wait = CrowdFor + TimeSpan.FromSeconds(5 * Math.Max(0, me.Number - 1));
        if (now - _crowdSince < wait || now - _lastChannelChange < CrowdCooldown)
            return null;
        if (state.Dead || _selling || _tidying || _cleaner.Active || _brain.Traveling || state.Attackers.Count > 0 || _channelSince != null)
            return null;
        _channelAvoid.Clear();
        string with = string.Join(", ", others.Where(o => o.Number < me.Number && o.Server == me.Server && o.Map == me.Map && o.Channel == me.Channel).Select(o => o.Character));
        _log($"👥 Bizim botumuz ({with}) aynı yerde, aynı kanalda ({me.Channel}); o kalıyor, bu bot başka kanala geçiyor.");
        _crowdSince = DateTime.MinValue;
        return StartChannelChange(state, p, now);
    }

    private static void WritePlace(BotPlace place)
    {
        try
        {
            Directory.CreateDirectory(ProfileStore.Directory);
            File.WriteAllText(PlaceFile(place.Number), JsonSerializer.Serialize(place));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The other SekoBots' places (fresh ones only).</summary>
    internal static List<BotPlace> ReadPlaces(int mine)
    {
        var places = new List<BotPlace>();
        for (int n = 1; n <= 12; n++)
        {
            if (n == mine)
                continue;
            try
            {
                string file = PlaceFile(n);
                if (File.Exists(file) && JsonSerializer.Deserialize<BotPlace>(File.ReadAllText(file)) is { } place
                    && DateTime.UtcNow - place.At < PlaceFresh)
                    places.Add(place);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return places;
    }
}
