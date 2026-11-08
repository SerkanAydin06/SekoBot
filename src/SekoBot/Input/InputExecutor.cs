using System.Runtime.InteropServices;
using System.Threading.Channels;
using SekoBot.Bot;
using SekoBot.Capture;
using SekoBot.Native;

namespace SekoBot.Input;

/// <summary>
/// Executes bot actions one after another on a background task.
/// Normally input goes through SendInput and only while the game window is in the
/// foreground. In background mode (experimental) keys and clicks are posted to the game
/// window as messages instead, so the game can stay behind other windows; held keys are
/// re-posted with the repeat flag like a real keyboard does. In dry-run mode actions are
/// only logged.
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
    private readonly bool _background;
    private readonly Task? _repeater;

    /// <param name="hasTarget">Whether a mob is selected (from memory); null when it can't be told.</param>
    /// <param name="missed">Called with a candidate that was clicked but selected nothing.</param>
    /// <param name="selected">Called with the spot whose click selected a mob.</param>
    /// <param name="background">Post messages to the game window instead of SendInput (experimental).</param>
    public InputExecutor(WindowInfo window, bool dryRun, Action<string> log, Func<bool?> hasTarget,
        Func<double?> targetHp, Action<Point> missed, Action<Point> selected, bool background = false)
    {
        _background = background && !dryRun;
        _targetHp = targetHp;
        _selected = selected;
        _window = window;
        DryRun = dryRun;
        _log = log;
        _hasTarget = hasTarget;
        _missed = missed;
        _worker = Task.Run(RunAsync);
        if (_background)
            _repeater = Task.Run(RepeatHeldKeysAsync);
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
                if (!_background && Win32.GetForegroundWindow() != _window.Handle)
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
            case ClickAction { Slow: true } button:
                await ClickButtonAsync(button.Client, token);
                break;
            case ClickAction click:
                await ClickAsync(click.Client, 30, 60, token);
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
    /// Clicks mobs (nearest first) and waits until memory shows one selected after each click.
    /// Stops at the first one that gets selected. A miss usually makes the character walk,
    /// which moves the camera and makes the other candidates stale, so give up after two.
    /// </summary>
    private async Task ClickCandidatesAsync(IReadOnlyList<Point> candidates, CancellationToken token)
    {
        int misses = 0;
        foreach (var spot in candidates)
        {
            if ((!_background && Win32.GetForegroundWindow() != _window.Handle) || misses >= 2)
                return;
            // A bar that was already showing before the click doesn't prove this click selected anything.
            bool hadTarget = _hasTarget() == true;
            double? before = _targetHp();
            await ClickAsync(spot, 40, 70, token);

            // When it can't be told whether something got selected, wait a bit longer.
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
            // Only a click that left nothing selected at all hit empty ground.
            if (_hasTarget() == false)
                _missed(spot);
            misses++;
        }
    }

    /// <summary>
    /// Clicks a game UI button (e.g. "Burada yeniden başla") the way a person does. The game's UI
    /// ignores clicks posted to a window in the background and clicks that are too quick, so:
    /// bring the game to the front for a moment, move the real cursor onto the button and let it
    /// hover, press with a real mouse event, hold, release, and wait for the game to take it.
    /// Afterwards the cursor and the window that was in front are put back.
    /// </summary>
    private async Task ClickButtonAsync(Point client, CancellationToken token)
    {
        Win32.GetCursorPos(out var previous);
        nint before = Win32.GetForegroundWindow();
        bool front = before == _window.Handle || BringGameToFront();
        if (front && before != _window.Handle)
            await Task.Delay(300, token);
        var screen = WindowService.ClientToScreen(_window, client);
        nint lParam = (nint)((client.Y << 16) | (client.X & 0xFFFF));
        try
        {
            // Come in with a few small moves so the button sees the cursor arrive (hover).
            for (int i = 0; i < 5; i++)
            {
                int d = 8 - i * 2;
                MoveMouse(new Point(screen.X + d, screen.Y + d / 2));
                Win32.PostMessage(_window.Handle, Win32.WM_MOUSEMOVE, 0, lParam);
                await Task.Delay(60, token);
            }
            MoveMouse(screen);
            await Task.Delay(300, token);
            if (front)
                SendMouse(Win32.MOUSEEVENTF_LEFTDOWN);
            else
                Win32.PostMessage(_window.Handle, Win32.WM_LBUTTONDOWN, Win32.MK_LBUTTON, lParam);
            await Task.Delay(250, token);
            if (front)
                SendMouse(Win32.MOUSEEVENTF_LEFTUP);
            else
                Win32.PostMessage(_window.Handle, Win32.WM_LBUTTONUP, 0, lParam);
            await Task.Delay(500, token); // keep the cursor there while the game handles it
        }
        finally
        {
            Win32.SetCursorPos(previous.X, previous.Y);
            if (front && before != _window.Handle && before != 0)
                Win32.SetForegroundWindow(before);
        }
        if (!front)
            _log("Oyun penceresi öne getirilemedi; tıklama arka plandan gönderildi.");
    }

    /// <summary>Puts the game window in front. Windows only lets a program do that right after
    /// a key event, hence the Alt tap first.</summary>
    private bool BringGameToFront()
    {
        var alt = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
        alt.u.ki.wVk = 0x12; // VK_MENU
        var altUp = alt;
        altUp.u.ki.dwFlags = Win32.KEYEVENTF_KEYUP;
        Win32.SendInput(2, [alt, altUp], Marshal.SizeOf<Win32.INPUT>());
        Win32.SetForegroundWindow(_window.Handle);
        return Win32.GetForegroundWindow() == _window.Handle;
    }

    /// <summary>Left click at a point of the game's client area.</summary>

    private async Task ClickAsync(Point client, int settleMin, int settleMax, CancellationToken token)
    {
        if (_background)
        {
            // Client coordinates in lParam; the real cursor is left alone.
            nint lParam = (nint)((client.Y << 16) | (client.X & 0xFFFF));
            Win32.PostMessage(_window.Handle, Win32.WM_MOUSEMOVE, 0, lParam);
            await Task.Delay(_random.Next(settleMin, settleMax), token);
            Win32.PostMessage(_window.Handle, Win32.WM_LBUTTONDOWN, Win32.MK_LBUTTON, lParam);
            await Task.Delay(_random.Next(35, 75), token);
            Win32.PostMessage(_window.Handle, Win32.WM_LBUTTONUP, 0, lParam);
            return;
        }
        MoveMouse(WindowService.ClientToScreen(_window, client));
        await Task.Delay(_random.Next(settleMin, settleMax), token);
        SendMouse(Win32.MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(_random.Next(35, 75), token);
        SendMouse(Win32.MOUSEEVENTF_LEFTUP);
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
        if (_background)
        {
            // Same lock as the repeater: a repeat can never slip in after this key's key-up.
            lock (_held)
            {
                bool wasDown = _held.Contains(key);
                PostKey(vk, scan, IsExtended(key), down, repeat: down && wasDown);
                if (down) _held.Add(key); else _held.Remove(key);
            }
            return;
        }
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

    private void PostKey(uint vk, ushort scan, bool extended, bool down, bool repeat) =>
        Win32.PostMessage(_window.Handle, down ? Win32.WM_KEYDOWN : Win32.WM_KEYUP, (nint)vk,
            KeyMessageLParam(scan, extended, down, repeat));

    /// <summary>
    /// lParam of WM_KEYDOWN/WM_KEYUP: repeat count 1, scan code (bits 16–23), extended
    /// (bit 24), previous state (bit 30: key was already down) and transition (bit 31: up).
    /// </summary>
    internal static nint KeyMessageLParam(ushort scan, bool extended, bool down, bool repeat)
    {
        long value = 1 | ((long)(scan & 0xFF) << 16);
        if (extended) value |= 1L << 24;
        if (!down || repeat) value |= 1L << 30;
        if (!down) value |= 1L << 31;
        return (nint)value;
    }

    /// <summary>
    /// A real keyboard repeats a held key ~30×/s. Messages carry no "is held" state, so a game
    /// reading them sees the key held only while key-down messages keep coming.
    /// </summary>
    private async Task RepeatHeldKeysAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, _cts.Token);
                lock (_held)
                {
                    foreach (var key in _held)
                    {
                        uint vk = (uint)(key & Keys.KeyCode);
                        PostKey(vk, (ushort)Win32.MapVirtualKey(vk, 0), IsExtended(key), down: true, repeat: true);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
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
        try { _repeater?.Wait(500); } catch { }
        _cts.Dispose();
    }
}
