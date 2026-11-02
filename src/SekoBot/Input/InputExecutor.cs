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
    private readonly MobCursors _cursors;
    private readonly Func<bool?> _hasTarget;
    private bool _warnedFocus, _warnedCursor;

    /// <param name="hasTarget">Whether a target bar is showing; null when it can't be told
    /// (target bar not known yet).</param>
    private readonly Func<int> _hoverDelayMs;

    public InputExecutor(WindowInfo window, bool dryRun, Action<string> log, MobCursors cursors, Func<bool?> hasTarget,
        Func<int>? hoverDelayMs = null)
    {
        _hoverDelayMs = hoverDelayMs ?? (() => 80);
        _window = window;
        DryRun = dryRun;
        _log = log;
        _cursors = cursors;
        _hasTarget = hasTarget;
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
                var screen = WindowService.ClientToScreen(_window, click.Client);
                Win32.SetCursorPos(screen.X, screen.Y);
                await Task.Delay(_random.Next(25, 55), token);
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
            case FindTargetAction:
                await FindTargetAsync(token);
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
    /// Hovers points around the character (nearest first) and reads the cursor. Clicks the first
    /// spot that shows a learned attack cursor, or a cursor different from the plain ground one,
    /// then checks whether a target bar appears to learn which cursor means "monster".
    /// </summary>
    private async Task FindTargetAsync(CancellationToken token)
    {
        if (!Win32.GetClientRect(_window.Handle, out var client))
            return;
        var points = MobSearch.ScanPoints(client.Right - client.Left, client.Bottom - client.Top);
        var seen = new List<nint>(points.Count);
        int pick = -1;
        foreach (var point in points)
        {
            if (Win32.GetForegroundWindow() != _window.Handle)
                return;
            MoveMouse(WindowService.ClientToScreen(_window, point));
            // The game only changes its cursor after it has noticed the mouse over the mob.
            await Task.Delay(Math.Clamp(_hoverDelayMs(), 20, 1000), token);
            seen.Add(Win32.CurrentCursor());
            pick = MobSearch.Choose(seen, _cursors);
            if (pick >= 0)
                break;
        }
        if (pick < 0)
            pick = MobSearch.Choose(seen, _cursors, final: true);

        if (seen.Count > 0 && seen.All(c => c == seen[0]))
        {
            if (!_warnedCursor)
            {
                _warnedCursor = true;
                _log(seen[0] == 0
                    ? "Oyun imleci okunamıyor (oyun kendi imlecini çiziyor olabilir); mob arama kör saldırıya döner."
                    : "Etrafta mob imleci görülmedi.");
            }
            return;
        }
        if (pick < 0)
            return;

        nint cursor = seen[pick];
        MoveMouse(WindowService.ClientToScreen(_window, points[pick]));
        await Task.Delay(_random.Next(25, 45), token);
        SendMouse(Win32.MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(_random.Next(35, 75), token);
        SendMouse(Win32.MOUSEEVENTF_LEFTUP);

        if (_hasTarget() == null)
            return; // Can't verify yet: the target bar hasn't been found.
        for (int waited = 0; waited < 1500; waited += 50)
        {
            if (_hasTarget() == true)
            {
                if (_cursors.MarkGood(cursor))
                    _log("Mob imleci öğrenildi ✓ Artık mobları kendisi bulup tıklayacak.");
                return;
            }
            await Task.Delay(50, token);
        }
        _cursors.MarkFailed(cursor);
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
