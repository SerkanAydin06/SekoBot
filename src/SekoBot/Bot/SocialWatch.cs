using SekoBot.Config;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>Something to tell the user. <paramref name="Urgent"/>: also an alert sound (a person may be checking on the bot).</summary>
internal readonly record struct SocialNotice(string Text, bool Urgent);

/// <summary>
/// Turns what the game shows around the fight into messages for the user: a private message, a
/// trade / party / friend request, a question of the game, our name (or a GM) in the chat, a drop
/// event starting, points left to spend. What was already there when watching started isn't told
/// (old messages); everything is told once. Pure logic.
/// </summary>
internal sealed class SocialWatch
{
    private readonly Dictionary<string, int> _threads = [];
    private readonly HashSet<(RequestKind, string)> _requests = [];
    private readonly HashSet<int> _questions = [];
    private readonly HashSet<int> _events = [];
    private int _statPoints, _skillPoints;
    private bool _started;
    /// <summary>Players we just wrote to (from Telegram): our own message isn't told back.</summary>
    private readonly Dictionary<string, DateTime> _ownTo = new(StringComparer.OrdinalIgnoreCase);

    public void ExpectOwn(string to, DateTime now) => _ownTo[to.Trim()] = now;

    /// <summary>The events going on now that the character's level allows (their items are picked up).</summary>
    public IReadOnlyList<GameEvent> ActiveEvents { get; private set; } = [];
    /// <summary>All events the game lists (now and coming).</summary>
    public IReadOnlyList<GameEvent> Events { get; private set; } = [];

    public void Reset()
    {
        _threads.Clear();
        _requests.Clear();
        _questions.Clear();
        _events.Clear();
        _statPoints = _skillPoints = 0;
        _started = false;
        _ownTo.Clear();
    }

    public List<SocialNotice> Update(SocialSnapshot social, BotProfile p, int level, DateTime now)
    {
        var notices = new List<SocialNotice>();
        bool first = !_started;
        _started = true;
        string me = social.OwnName;

        // Private messages: the new ones in each conversation, not our own.
        foreach (var thread in social.Whispers)
        {
            int before = _threads.TryGetValue(thread.With, out int seen) ? seen : first ? thread.Count : 0;
            _threads[thread.With] = thread.Count;
            if (thread.Count <= before || !p.NotifyWhispers)
                continue;
            var fresh = thread.Last.TakeLast(Math.Min(thread.Count - before, thread.Last.Count)).Where(m => m.Message.Length > 0).ToList();
            foreach (var message in fresh)
                if (!SameName(message.From, me))
                    notices.Add(new($"💬 Fısıltı — {Who(message.From.Length > 0 ? message.From : thread.With, message.Level)}: {message.Message}", true));
            // The message itself isn't always readable (the DM window keeps only how many there are):
            // still told — unless it was our own answer.
            if (fresh.Count == 0)
            {
                if (_ownTo.TryGetValue(thread.With, out var sent) && now - sent < TimeSpan.FromMinutes(2))
                    _ownTo.Remove(thread.With);
                else
                    notices.Add(new($"💬 Fısıltı — {Who(thread.With, thread.Level)}: yeni mesaj yazdı (oyunda mesaj penceresinde).", true));
            }
        }

        // Requests: told when they show up; forgotten when they go, so a new one is told again.
        var requests = social.Requests.Select(r => (r.Kind, r.Name)).ToHashSet();
        foreach (var request in social.Requests)
            if (_requests.Add((request.Kind, request.Name)) && !first && p.NotifyRequests)
                notices.Add(new($"{request.Kind switch { RequestKind.Trade => "🤝 Takas isteği", RequestKind.Party => "👥 Grup daveti", _ => "🙋 Arkadaşlık isteği" }}: " +
                                Who(request.Name, request.Level), true));
        _requests.IntersectWith(requests);

        foreach (var question in social.Questions)
            if (_questions.Add(question.Id) && !first && p.NotifyRequests)
                notices.Add(new("❓ Oyun soruyor: " + question.Text, true));
        _questions.IntersectWith(social.Questions.Select(q => q.Id));

        // The chat: our name written by someone else, or a GM writing.
        if (p.NotifyMentions)
            foreach (var line in social.NewChat)
            {
                if (line.Sender == -1 || line.Name.Length == 0 || SameName(line.Name, me))
                    continue;
                if (OtherPlayer.IsGmName(line.Name))
                    notices.Add(new($"⚠ GM sohbette yazdı — {line.Name}: {line.Message}", true));
                else if (Mentions(line.Message, me))
                    notices.Add(new($"📣 Sohbette adın geçti — {Who(line.Name, line.Level)}: {line.Message}", true));
            }

        // Drop events: the items of those going on now (level allowing) are picked up.
        Events = social.Events;
        ActiveEvents = [.. social.Events.Where(e => (e.Active || now >= e.Start && now < e.End) && level >= e.MinLevel)];
        foreach (var e in ActiveEvents)
            if (_events.Add(e.Id) && p.NotifyEvents)
                notices.Add(new($"🎉 Etkinlik: {ItemBook.NameOf(e.Item)} düşüyor ({e.End:HH:mm}'e kadar)" +
                                (p.EventLoot ? "; düşerse toplanacak." : "."), false));

        // Points to spend: told when there are more than before.
        if (p.NotifyPoints && (social.StatPoints > _statPoints || social.SkillPoints > _skillPoints) && (social.StatPoints > 0 || social.SkillPoints > 0))
            notices.Add(new("⬆ Dağıtılmamış puan var: " + string.Join(", ",
                new[] { social.StatPoints > 0 ? $"{social.StatPoints} statü" : "", social.SkillPoints > 0 ? $"{social.SkillPoints} beceri" : "" }
                    .Where(t => t.Length > 0)) + " puanı.", false));
        _statPoints = social.StatPoints;
        _skillPoints = social.SkillPoints;
        return notices;
    }

    private static string Who(string name, int level) => level is > 0 and < 1000 ? $"{name} (Sv {level})" : name;

    private static bool SameName(string a, string b) => b.Length > 0 && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The name in a message, also without its digits ("KAiZER06" → "kaizer"), as a word.</summary>
    internal static bool Mentions(string message, string name)
    {
        if (name.Length < 3)
            return false;
        string bare = name.TrimEnd("0123456789".ToCharArray());
        var culture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        foreach (string key in new[] { name, bare }.Where(k => k.Length >= 4).Distinct())
        {
            int at = culture.CompareInfo.IndexOf(message, key, System.Globalization.CompareOptions.IgnoreCase);
            if (at >= 0 && (at == 0 || !char.IsLetter(message[at - 1])))
                return true;
        }
        return false;
    }
}
