using System.Runtime.InteropServices;
using System.Threading.Channels;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Native;

namespace SekoBot.Input;

/// <summary>
/// Executes bot actions one after another on a background task.
/// Input is only sent while the game window is in the foreground; in dry-run
/// mode actions are only logged.
/// </summary>
internal sealed class InputExecutor : IDisposable
{
    private readonly WindowInfo _window;
    private readonly Action<string> _log;
    private readonly Channel<BotAction> _queue = Channel.CreateBounded<BotAction>(
        new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<Keys> _held = [];
    private readonly Random _random = new();
    private readonly Task _worker;
    private readonly Func<bool?> _hasTarget;
    private readonly Action<Point> _missed;
    private readonly Action<Point> _selected;
    private readonly Func<double?> _targetHp;
    private bool _warnedFocus, _learnedLogged;

    /// <param name="hasTarget">Whether a target bar is showing; null when it can't be told
    /// (target bar not known yet).</param>
    /// <param name="missed">Called with a candidate that was clicked but selected nothing.</param>
    /// <param name="selected">Called with the spot whose click selected a mob.</param>
    public InputExecutor(WindowInfo window, bool dryRun, Action<string> log, Func<bool?> hasTarget,
        Func<double?> targetHp, Action<Point> missed, Action<Point> selected)
    {
        _targetHp = targetHp;
        _selected = selected;
        _window = window;
        DryRun = dryRun;
        _log = log;
        _hasTarget = hasTarget;
        _missed = missed;
        _worker = Task.Run(RunAsync);
    }

    public bool DryRun { get; }

    public void Enqueue(BotAction action) => _queue.Writer.TryWrite(action);

    private async Task RunAsync()
    {
        try
        {
            await foreach (var action in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                if (DryRun)
                {
                    _log("[Kuru] " + action);
                    continue;
                }
                if (action is KeyUpAction up)
                {
                    // Releasing is always safe and must never be skipped.
                    bool held;
                    lock (_held) held = _held.Contains(up.Key);
                    if (held) SendKey(up.Key, down: false);
                    continue;
                }
                if (Win32.GetForegroundWindow() != _window.Handle)
                {
                    if (!_warnedFocus)
                    {
                        // Don't leave the attack key pressed in whatever app is in front now.
                        ReleaseAll();
                        _log("Oyun penceresi odakta değil; girdiler bekletiliyor.");
                    }
                    _warnedFocus = true;
                    continue;
                }
                _warnedFocus = false;
                await ExecuteAsync(action, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log("Girdi hatası: " + ex.Message);
        }
        finally
        {
            ReleaseAll();
        }
    }

    private async Task ExecuteAsync(BotAction action, CancellationToken token)
    {
        switch (action)
        {
            case ClickAction click:
                MoveMouse(WindowService.ClientToScreen(_window, click.Client));
                await Task.Delay(_random.Next(30, 60), token);
                SendMouse(Win32.MOUSEEVENTF_LEFTDOWN);
                await Task.Delay(_random.Next(35, 75), token);
                SendMouse(Win32.MOUSEEVENTF_LEFTUP);
                break;
            case KeyTapAction tap:
                SendKey(tap.Key, down: true);
                await Task.Delay(_random.Next(40, 90), token);
                SendKey(tap.Key, down: false);
                break;
            case KeyDownAction keyDown:
                SendKey(keyDown.Key, down: true);
                break;
            case FindTargetAction find:
                await ClickCandidatesAsync(find.Candidates, token);
                break;
            case KeyHoldAction hold:
                SendKey(hold.Key, down: true);
                try { await Task.Delay(hold.Milliseconds, token); }
                finally { SendKey(hold.Key, down: false); }
                break;
        }
        await Task.Delay(_random.Next(30, 70), token);
    }

    /// <summary>
    /// Clicks probable mobs (nearest first) and waits for a target bar after each click.
    /// Stops at the first one that gets selected. A miss usually makes the character walk,
    /// which moves the camera and makes the other candidates stale, so give up after two.
    /// </summary>
    private async Task ClickCandidatesAsync(IReadOnlyList<Point> candidates, CancellationToken token)
    {
        int misses = 0;
        foreach (var spot in candidates)
        {
            if (Win32.GetForegroundWindow() != _window.Handle || misses >= 2)
                return;
            // A bar that was already showing before the click doesn't prove this click selected anything.
            bool hadTarget = _hasTarget() == true;
            double? before = _targetHp();
            MoveMouse(WindowService.ClientToScreen(_window, spot));
            await Task.Delay(_random.Next(40, 70), token);
            SendMouse(Win32.MOUSEEVENTF_LEFTDOWN);
            await Task.Delay(_random.Next(35, 75), token);
            SendMouse(Win32.MOUSEEVENTF_LEFTUP);

            // Until the target bar has been found once, give the bar finder time to spot it.
            int wait = _hasTarget() == null ? 1500 : 800;
            for (int waited = 0; waited < wait; waited += 50)
            {
                bool fresh = !hadTarget ||
                    (before is double b && _targetHp() is double n && Math.Abs(n - b) > 0.02);
                if (_hasTarget() == true && fresh)
                {
                    _selected(spot);
                    if (!_learnedLogged)
                    {
                        _learnedLogged = true;
                        _log("Mob bulundu ve seçildi ✓");
                    }
                    return;
                }
                await Task.Delay(50, token);
            }
            // Only a click that left no target bar at all hit empty ground.
            if (_hasTarget() == false)
                _missed(spot);
            misses++;
        }
    }

    /// <summary>
    /// Moves the cursor and also sends a real mouse-move input event: some games ignore a
    /// bare cursor jump and only react (hover, attack cursor) to actual mouse input.
    /// </summary>
    private static void MoveMouse(Point screen)
    {
        Win32.SetCursorPos(screen.X, screen.Y);
        int vx = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN), vy = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
        int vw = Math.Max(2, Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN));
        int vh = Math.Max(2, Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));
        var input = new Win32.INPUT { type = Win32.INPUT_MOUSE };
        input.u.mi.dx = (int)((screen.X - vx) * 65535L / (vw - 1));
        input.u.mi.dy = (int)((screen.Y - vy) * 65535L / (vh - 1));
        input.u.mi.dwFlags = Win32.MOUSEEVENTF_MOVE | Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
        Win32.SendInput(1, [input], Marshal.SizeOf<Win32.INPUT>());
    }

    private static void SendMouse(uint flags)
    {
        var input = new Win32.INPUT { type = Win32.INPUT_MOUSE };
        input.u.mi.dwFlags = flags;
        Win32.SendInput(1, [input], Marshal.SizeOf<Win32.INPUT>());
    }

    private void SendKey(Keys key, bool down)
    {
        uint vk = (uint)(key & Keys.KeyCode);
        // Games read DirectInput scan codes, so send the scan code rather than the virtual key.
        ushort scan = (ushort)Win32.MapVirtualKey(vk, 0);
        uint flags = Win32.KEYEVENTF_SCANCODE | (down ? 0 : Win32.KEYEVENTF_KEYUP);
        if (IsExtended(key))
            flags |= Win32.KEYEVENTF_EXTENDEDKEY;
        var input = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
        input.u.ki.wScan = scan;
        input.u.ki.dwFlags = flags;
        Win32.SendInput(1, [input], Marshal.SizeOf<Win32.INPUT>());
        lock (_held)
        {
            if (down) _held.Add(key); else _held.Remove(key);
        }
    }

    private static bool IsExtended(Keys key) => key is Keys.Left or Keys.Right or Keys.Up or Keys.Down
        or Keys.Insert or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;

    /// <summary>Never leave a key stuck down when the bot stops.</summary>
    private void ReleaseAll()
    {
        Keys[] held;
        lock (_held) held = [.. _held];
        foreach (var key in held)
            SendKey(key, down: false);
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _cts.Cancel();
        try { _worker.Wait(1500); } catch { }
        _cts.Dispose();
    }
}
