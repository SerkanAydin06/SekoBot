namespace SekoBot.Bot;

internal abstract record BotAction;

/// <summary>Left click at a point in game client pixels.</summary>
/// <param name="Slow">For UI buttons (e.g. "Burada yeniden başla"): the real cursor is put on
/// the button, hovers a moment, and the button is held down a bit, like a person clicks.</param>
/// <param name="Fast">With <paramref name="Slow"/>: the same careful click (game in front, hover first) with shorter waits — for the item menu and its question while throwing items away.</param>
/// <param name="Yield">A long job's click (the market scan): another SekoBot waiting for the mouse goes first.</param>
/// <param name="StillWanted">Asked right before the press (after waiting for the mouse): false = no click at all
/// (the window it was for has closed meanwhile; a click then would land on the ground and walk the character).</param>
internal sealed record ClickAction(Point Client, bool Slow = false, bool Fast = false, bool Yield = false, Func<bool>? StillWanted = null) : BotAction
{
    public override string ToString() => $"Tıkla ({Client.X}, {Client.Y}){(Slow ? " yavaş" : "")}";
}

/// <summary>Drag with the left button held, like a person does (an item out of the bag onto the
/// ground). Points in game client pixels; the game is brought to the front for it.</summary>
internal sealed record MouseDragAction(Point From, Point To) : BotAction
{
    public override string ToString() => $"Sürükle ({From.X}, {From.Y}) → ({To.X}, {To.Y})";
}

/// <summary>Types text into the game (the box that has the focus), letter by letter; the game is
/// brought to the front for it. <paramref name="ClearFirst"/>: whatever is in the box goes first (Ctrl+A,
/// Backspace) — a movement key still on its way can land in it (seen: "sAs" for "As").</summary>
internal sealed record TypeTextAction(string Text, bool ClearFirst = false) : BotAction
{
    public override string ToString() => $"Yaz \"{Text}\"";
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
