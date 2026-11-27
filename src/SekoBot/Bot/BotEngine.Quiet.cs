namespace SekoBot.Bot;

/// <summary>
/// Before something that leaves the character standing (an update of PARDUS, a break out of the game): what attacks
/// it is fought, nothing new gathered, then it walks to a quiet spot away from the mobs (<see cref="Brain.WantsQuietFor"/>).
/// </summary>
internal sealed partial class BotEngine
{
    /// <summary>A trip to a market NPC going on (there, selling, or back to the farm).</summary>
    public bool SellingNow => _selling;

    /// <summary>Asks for a quiet spot (<paramref name="why"/>: in words; null: not any more). True once the character
    /// stands there (nothing near, nothing attacking it, no selling or throwing going on).</summary>
    public bool QuietFor(string? why)
    {
        if (_brain.WantsQuietFor != why)
            _brain.WantsQuietFor = why;
        return why != null && BotRunning && _brain.ReadyToClean && !_selling && !_cleaner.Active && World is { Dead: false };
    }
}
