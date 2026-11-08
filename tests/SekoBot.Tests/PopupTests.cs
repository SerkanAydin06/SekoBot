using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Recording;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class PopupTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Advert_after_level_up_is_seen()
    {
        using var frame = RecordingFormat.LoadPng(Fixture("levelup-popup.png"));
        Assert.True(PopupDetector.IsOpen(frame));
    }

    [Fact]
    public void Esc_system_menu_over_the_blurred_game_is_seen()
    {
        using var frame = RecordingFormat.LoadPng(Fixture("esc-menu.png"));
        Assert.True(PopupDetector.IsOpen(frame));
    }

    [Theory]
    [InlineData("topdown-wolves.png")]
    [InlineData("red-armor.png")]
    [InlineData("other-player.png")]
    [InlineData("trees-preview.png")] // big blurry leaves in front of the camera (preview, scaled up)
    [InlineData("death-screen.png")]  // handled by the death logic, not by Esc
    public void Plain_game_screen_has_no_popup(string file)
    {
        using var frame = RecordingFormat.LoadPng(Fixture(file));
        Assert.False(PopupDetector.IsOpen(frame));
    }

    [Fact]
    public void Bot_presses_esc_once_and_only_again_if_the_window_is_still_there()
    {
        var p = new BotProfile { LeashMeters = 0, SkillEverySec = new int[5] };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        GameState At(double ms, bool popup, params Point[] mobs) =>
            new(TimeSpan.FromMilliseconds(ms), 1920, 1080, 1, 1, null, 1) { PopupOpen = popup, MobCandidates = mobs };

        brain.Tick(At(0, false));
        // Walking to a mob when the advert pops up.
        brain.Tick(At(100, false, new Point(1500, 540)));
        var first = brain.Tick(At(200, true));
        Assert.Contains(first, a => a is KeyTapAction t && t.Key == Keys.Escape);
        Assert.Contains(first, a => a is KeyUpAction u && u.Key == p.Keys.MoveRight);

        var actions = new List<BotAction>();
        for (double t = 300; t < 3300; t += 100)
            actions.AddRange(brain.Tick(At(t, true)));
        Assert.Single(actions.OfType<KeyTapAction>(), a => a.Key == Keys.Escape); // once more, after 3 s
        Assert.DoesNotContain(actions, a => a is KeyDownAction);

        // Closed: back to work.
        var after = brain.Tick(At(3400, false, new Point(1500, 540)));
        Assert.Contains(after, a => a is KeyDownAction d && d.Key == p.Keys.MoveRight);
    }
}
