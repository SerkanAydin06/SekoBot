namespace SekoBot.Memory;

/// <summary>A private message ("fısıltı") as the game keeps it.</summary>
internal readonly record struct Whisper(string From, int Level, string Message);

/// <summary>A conversation in the private messages window: the other player and the messages so far
/// (<paramref name="Count"/> all of them, <paramref name="Last"/> the newest few).</summary>
internal sealed record WhisperThread(string With, int Count, IReadOnlyList<Whisper> Last);

/// <summary>Someone asking for something: a trade, a party, a friendship.</summary>
internal enum RequestKind { Trade, Party, Friend }

internal readonly record struct PlayerRequest(RequestKind Kind, string Name, int Level);

/// <summary>A yes / no question the game shows (e.g. an invitation).</summary>
internal readonly record struct GameQuestion(int Id, string Text);

/// <summary>A line of the chat. <paramref name="Sender"/>: -1 the game's own messages ("27 Yang Kazandın").</summary>
internal readonly record struct ChatLine(int Sender, string Name, int Level, string Message);

/// <summary>An hourly drop event: this item drops (up to <paramref name="Count"/>) for characters of
/// at least <paramref name="MinLevel"/> between the times (the game's clock).</summary>
internal readonly record struct GameEvent(int Id, int Item, int Count, int MinLevel, bool Active, DateTime Start, DateTime End);

/// <summary>
/// Everything around the character that isn't the fight: messages, requests, questions, new chat
/// lines (only those that came since the last read), the drop events and points left to spend.
/// </summary>
internal sealed record SocialSnapshot(string OwnName, IReadOnlyList<WhisperThread> Whispers, IReadOnlyList<PlayerRequest> Requests,
    IReadOnlyList<GameQuestion> Questions, IReadOnlyList<ChatLine> NewChat, IReadOnlyList<GameEvent> Events,
    int StatPoints, int SkillPoints);
