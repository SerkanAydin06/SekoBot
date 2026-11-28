using SekoBot.Bot;
using Xunit;

namespace SekoBot.Tests;

/// <summary>A listed window Esc doesn't reach (the game's menu comes up instead) is left out.</summary>
public class PopupGuardTests
{
    [Fact]
    public void A_window_whose_Esc_opens_the_menu_is_ignored_until_it_leaves_the_list()
    {
        var guard = new PopupGuard();
        var (w1, told1) = guard.Filter(["popup_C"]);
        Assert.Equal("popup_C", PopupWatch.Target(w1));
        Assert.Null(told1);
        guard.Closing("popup_C", withEsc: true);
        // Esc fell through: the menu is up, the advert still listed.
        var (w2, told2) = guard.Filter(["popup_C", "EscMenu"]);
        Assert.Equal("popup_C", told2);
        Assert.Equal("EscMenu", PopupWatch.Target(w2));
        guard.Closing("EscMenu", withEsc: true);
        var (w3, _) = guard.Filter(["popup_C"]);
        Assert.Null(PopupWatch.Target(w3));
        // Gone from the list, then back (a new advert): watched again.
        guard.Filter([]);
        var (w4, _) = guard.Filter(["popup_C"]);
        Assert.Equal("popup_C", PopupWatch.Target(w4));
    }

    [Fact]
    public void A_window_closed_by_Esc_is_not_ignored()
    {
        var guard = new PopupGuard();
        guard.Filter(["events"]);
        guard.Closing("events", withEsc: true);
        var (w, told) = guard.Filter([]);
        Assert.Null(told);
        var (again, _) = guard.Filter(["events", "EscMenu"]);
        Assert.Contains("events", again!);
    }
}
