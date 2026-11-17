using SekoBot.Bot;
using SekoBot.Config;
using SekoBot.Memory;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

/// <summary>Keys stay off while the message window is open; the camera is set again only when it moved.</summary>
public class TypingTests
{
    private static readonly Vec3 Me = new(10000, 10000, 1000);

    private static GameState State(double ms, bool typing = false, double pitch = -89, double height = 2000) =>
        new(TimeSpan.FromMilliseconds(ms), 1920, 1080, 1, 1, 0, 1)
        {
            Walking = true, Position = Me, Typing = typing, Camera = new CameraView(Me with { Z = Me.Z + height }, pitch, 0, 0, 90),
        };

    [Fact]
    public void No_key_is_pressed_while_the_message_window_is_open()
    {
        var p = new BotProfile { LeashMeters = 0 };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        var actions = new List<BotAction>();
        for (double t = 0; t < 40_000; t += 100)
            actions.AddRange(brain.Tick(State(t, typing: true)));
        Assert.DoesNotContain(actions, a => a is KeyTapAction or KeyHoldAction or KeyDownAction or TypeTextAction or ClickAction);
        Assert.Contains(actions.OfType<NoteAction>(), n => n.Text.Contains("mesaj penceresi açık"));
        var after = brain.Tick(State(40_100));
        Assert.Contains(after.OfType<NoteAction>(), n => n.Text.Contains("kapandı"));
    }

    private static int CameraPresses(Func<double, GameState> scene)
    {
        var p = new BotProfile { LeashMeters = 0 };
        var brain = new Brain(() => p);
        brain.Start(TimeSpan.Zero);
        int presses = 0;
        for (double t = 0; t < 100_000; t += 100)
            presses += brain.Tick(scene(t)).Count(a => a is KeyDownAction d && d.Key == p.Keys.CameraTop);
        return presses;
    }

    [Fact]
    public void The_camera_is_set_once_and_again_only_when_it_moved()
    {
        // A setup holds the key 1.5 s (sent again after a second): two key-downs per setup.
        int once = CameraPresses(t => State(t));                                               // stays as set: only at the start
        Assert.Equal(2, once);
        Assert.Equal(2 * once, CameraPresses(t => State(t, pitch: t < 40_000 ? -89 : -40)));   // tilted: set again, once
        Assert.Equal(2 * once, CameraPresses(t => State(t, height: t < 40_000 ? 2000 : 900))); // zoomed in: set again, once
    }
}
