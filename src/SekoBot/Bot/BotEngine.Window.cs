using SekoBot.UI;

namespace SekoBot.Bot;

/// <summary>
/// The game made big enough for its windows while the bag is emptied or items are sold: a game arranged small
/// (358 pixels high) cuts its screens off — the game doesn't shrink them below 480 (seen: part of the bag out of
/// the window and the bot clicking beside the items). Grown in its place to 480 high just for the work (the turn
/// is this bot's then, its game in front anyway), and put back where it was afterwards. Arranged 640×480 (up to 6
/// games on the screen) nothing grows; only with more games, arranged smaller (the user's choice for 7+ games).
/// </summary>
internal sealed partial class BotEngine
{
    private Rectangle _grownFrom, _grownTo;
    private DateTime _grownAt;
    /// <summary>After growing, the game lays its screens out again: this long before they are read.</summary>
    internal static readonly TimeSpan GrowSettle = TimeSpan.FromSeconds(1);

    /// <summary>The game grown for the work (when it is small). True while its screens are still settling.</summary>
    private bool GrowForWork()
    {
        if (_grownTo.IsEmpty)
        {
            var before = WindowLayout.Visible(_window.Handle);
            var to = WindowLayout.GrowForUi(_window.Handle);
            if (to.IsEmpty)
                return false; // big enough already
            _grownFrom = before;
            _grownTo = to;
            _grownAt = DateTime.Now;
            _log($"Envanter tam görünsün diye oyun bu iş için büyütüldü ({to.Width}×{to.Height}); bitince eski yerine konacak.");
        }
        return DateTime.Now - _grownAt < GrowSettle;
    }

    /// <summary>The game back where it was (unless the user put it elsewhere meanwhile).</summary>
    private void ShrinkAfterWork()
    {
        if (_grownTo.IsEmpty)
            return;
        var now = WindowLayout.Visible(_window.Handle);
        bool same = Math.Abs(now.Left - _grownTo.Left) < 12 && Math.Abs(now.Top - _grownTo.Top) < 12 &&
                    Math.Abs(now.Width - _grownTo.Width) < 12 && Math.Abs(now.Height - _grownTo.Height) < 12;
        if (same)
            WindowLayout.PutBack(_window.Handle, _grownFrom);
        _grownTo = Rectangle.Empty;
    }
}
