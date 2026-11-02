namespace SekoBot.Bot;

internal abstract record BotAction;

/// <summary>Left click at a point in game client pixels.</summary>
internal sealed record ClickAction(Point Client) : BotAction
{
    public override string ToString() => $"Tıkla ({Client.X}, {Client.Y})";
}

internal sealed record KeyTapAction(Keys Key) : BotAction
{
    public override string ToString() => $"Tuş {Key}";
}

internal sealed record KeyHoldAction(Keys Key, int Milliseconds) : BotAction
{
    public override string ToString() => $"Basılı tut {Key} {Milliseconds} ms";
}

internal sealed record StopAction(string Reason) : BotAction
{
    public override string ToString() => "Dur: " + Reason;
}

internal enum BotPhase
{
    Stopped,
    Searching,
    Approaching,
    Attacking,
    Looting
}

internal static class BotPhaseText
{
    public static string ToTurkish(this BotPhase phase) => phase switch
    {
        BotPhase.Searching => "Metin aranıyor",
        BotPhase.Approaching => "Metne gidiliyor",
        BotPhase.Attacking => "Saldırıyor",
        BotPhase.Looting => "Eşya toplanıyor",
        _ => "Durduruldu"
    };
}
