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

/// <summary>Hover around the character until the cursor shows a monster, then click it.</summary>
internal sealed record FindTargetAction : BotAction
{
    public override string ToString() => "Mob ara";
}

internal sealed record StopAction(string Reason) : BotAction
{
    public override string ToString() => "Dur: " + Reason;
}

internal enum BotPhase
{
    Stopped,
    Searching,
    Fighting,
    Looting,
    Turning
}

internal static class BotPhaseText
{
    public static string ToTurkish(this BotPhase phase) => phase switch
    {
        BotPhase.Searching => "Mob arıyor",
        BotPhase.Fighting => "Saldırıyor",
        BotPhase.Looting => "Eşya toplanıyor",
        BotPhase.Turning => "Yön değiştiriyor",
        _ => "Durduruldu"
    };
}
