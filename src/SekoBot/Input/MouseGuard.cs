using System.Runtime.InteropServices;

namespace SekoBot.Input;

/// <summary>
/// While the bot moves the real cursor (a click on a game button, dragging an item, throwing),
/// the user's own mouse is held still: its moves and clicks are dropped so they can't mix with the
/// bot's (seen: a click of the user's landed in the middle of a drag). Only real mouse input is
/// dropped — the bot's own (injected) input passes, the keyboard is never touched (F11 always
/// works) — and only for a moment: at most <see cref="MaxHold"/>, then the mouse is free again
/// even if something went wrong. A low-level mouse hook on a thread of its own.
/// </summary>
internal static class MouseGuard
{
    /// <summary>Never held longer than this in one go.</summary>
    public static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(6);

    private static int _holds;
    private static long _untilTicks;
    private static Thread? _thread;
    private static readonly object Gate = new();
    private static HookProc? _proc; // kept alive: the hook calls it

    /// <summary>Holds the user's mouse until the returned object is disposed (or <see cref="MaxHold"/> passed).</summary>
    public static IDisposable Hold() => Hold(MaxHold);

    /// <summary>Holds the user's mouse for a whole job (all the items thrown, a sale): at most <paramref name="most"/>.
    /// A shorter hold meanwhile doesn't end it sooner.</summary>
    public static IDisposable Hold(TimeSpan most)
    {
        Start();
        Interlocked.Increment(ref _holds);
        long until = DateTime.UtcNow.Add(most).Ticks;
        long now;
        do
            now = Interlocked.Read(ref _untilTicks);
        while (until > now && Interlocked.CompareExchange(ref _untilTicks, until, now) != now);
        return new Release();
    }

    /// <summary>True while the user's mouse is held (for the hook, and for tests).</summary>
    internal static bool Holding => Volatile.Read(ref _holds) > 0 && DateTime.UtcNow.Ticks < Interlocked.Read(ref _untilTicks);

    private sealed class Release : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                Interlocked.Decrement(ref _holds);
        }
    }

    private static void Start()
    {
        lock (Gate)
        {
            if (_thread != null)
                return;
            _thread = new Thread(Run) { IsBackground = true, Name = "SekoBot mouse guard" };
            _thread.Start();
        }
    }

    private static void Run()
    {
        _proc = Hook;
        nint hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (hook == 0)
            return; // not allowed: the mouse simply isn't held
        try
        {
            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
        }
    }

    private static nint Hook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && Holding)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((info.flags & LLMHF_INJECTED) == 0)
                return 1; // the user's own mouse: dropped while the bot uses it
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private const int WH_MOUSE_LL = 14;
    private const uint LLMHF_INJECTED = 0x1;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG msg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
