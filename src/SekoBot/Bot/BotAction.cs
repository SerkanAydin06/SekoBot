namespace SekoBot.Bot;

internal abstract record BotAction;

/// <summary>Left click at a point in game client pixels.</summary>
/// <param name="Slow">For UI buttons (e.g. "Burada yeniden başla"): the real cursor is put on
/// the button, hovers a moment, and the button is held down a bit, like a person clicks.</param>
internal sealed record ClickAction(Point Client, bool Slow = false) : BotAction
{
    public override string ToString() => $"Tıkla ({Client.X}, {Client.Y}){(Slow ? " yavaş" : "")}";
}

/// <summary>Drag with the left button held, like a person does (an item out of the bag onto the
/// ground). Points in game client pixels; the game is brought to the front for it.</summary>
internal sealed record MouseDragAction(Point From, Point To) : BotAction
{
    public override string ToString() => $"Sürükle ({From.X}, {From.Y}) → ({To.X}, {To.Y})";
}

internal sealed record KeyTapAction(Keys Key) : BotAction
{
    public override string ToString() => $"Tuş {Key}";
}

internal sealed record KeyDownAction(Keys Key) : BotAction
{
    public override string ToString() => $"Bas {Key}";
}

internal sealed record KeyUpAction(Keys Key) : BotAction
{
    public override string ToString() => $"Bırak {Key}";
}

internal sealed record KeyHoldAction(Keys Key, int Milliseconds) : BotAction
{
    public override string ToString() => $"Basılı tut {Key} {Milliseconds} ms";
}

/// <summary>Click the probable mobs (nearest first) until one gets selected (memory shows a selected mob).</summary>
internal sealed record FindTargetAction(IReadOnlyList<Point> Candidates) : BotAction
{
    public override string ToString() => $"Mob ara ({Candidates.Count} aday)";
}

/// <summary>A message for the log (why the bot did something); not sent to the game.</summary>
internal sealed record NoteAction(string Text) : BotAction
{
    public override string ToString() => "Not: " + Text;
}

internal sealed record StopAction(string Reason) : BotAction
{
    public override string ToString() => "Dur: " + Reason;
}

internal enum BotPhase
{
    Stopped,
    Setup,
    Searching,
    Moving,
    Fighting,
    Looting,
    Turning,
    Dead,
    Recovering
}

internal static class BotPhaseText
{
    public static string ToTurkish(this BotPhase phase) => phase switch
    {
        BotPhase.Setup => "Kamera ayarlanıyor",
        BotPhase.Searching => "Mob arıyor",
        BotPhase.Moving => "Moba yürüyor",
        BotPhase.Fighting => "Saldırıyor",
        BotPhase.Looting => "Eşya toplanıyor",
        BotPhase.Turning => "Yön değiştiriyor",
        BotPhase.Dead => "Öldü, yeniden doğma bekleniyor",
        BotPhase.Recovering => "Can dolduruluyor",
        _ => "Durduruldu"
    };
}
