using System.Runtime.InteropServices;
using SekoBot.Capture;
using SekoBot.Native;

namespace SekoBot.Input;

/// <summary>What became of a touch.</summary>
internal enum TouchResult
{
    /// <summary>Touched.</summary>
    Done,
    /// <summary>Covered by another window: the click was posted to the game (it doesn't always take: check, try again slower).</summary>
    Posted,
    /// <summary>The game is minimized: not touched.</summary>
    Covered,
    /// <summary>The user kept using the mouse the whole time: not touched.</summary>
    Busy,
    /// <summary>Windows doesn't allow touches from this program.</summary>
    Unavailable,
}

/// <summary>
/// Opens a market stall without the game in front and without taking the mouse:
/// <list type="bullet">
/// <item>The stall shows on the screen: a finger tap there (the game is a mobile game and takes
/// touches). Windows puts the cursor on the tapped point for a moment; it is put back at once (~15 ms).</item>
/// <item>Another window covers it: the click is posted to the game window itself, with the cursor put
/// on the stall's place for ~90 ms (the game looks where the cursor is; seen: under 55 ms the click
/// is lost), then back. What lies over the game doesn't matter then.</item>
/// </list>
/// Only when the user isn't moving the mouse or holding a button (their click never goes astray).
/// </summary>
internal static class TouchTap
{
    /// <summary>The user's mouse has to be still this long before a tap.</summary>
    internal const int StillMs = 250;
    /// <summary>Waiting for the mouse to be still gives up after this long.</summary>
    internal const int WaitStillMs = 8000;

    private static readonly object Gate = new();
    private static bool? _ready;

    /// <param name="slower">A posted click that didn't take: the cursor stays longer on the stall's place.</param>
    public static async Task<TouchResult> TapAsync(WindowInfo window, Point client, CancellationToken token, bool slower = false)
    {
        if (!Ready())
            return TouchResult.Unavailable;
        if (Win32.IsIconic(window.Handle))
            return TouchResult.Covered;
        var screen = WindowService.ClientToScreen(window, client);
        // Wait until the user's mouse is still and no button is held (their click never goes astray).
        Win32.GetCursorPos(out var last);
        int still = 0;
        for (int waited = 0; still < StillMs; waited += 50)
        {
            if (waited >= WaitStillMs)
                return TouchResult.Busy;
            await Task.Delay(50, token);
            Win32.GetCursorPos(out var now);
            bool held = (N.GetAsyncKeyState(0x01) & 0x8000) != 0 || (N.GetAsyncKeyState(0x02) & 0x8000) != 0 || (N.GetAsyncKeyState(0x04) & 0x8000) != 0;
            still = now.X == last.X && now.Y == last.Y && !held ? still + 50 : 0;
            last = now;
        }
        if (Win32.IsIconic(window.Handle))
            return TouchResult.Covered;
        if (!Shows(window, screen))
        {
            PostedClick(window, client, screen, slower ? SlowerPostedClickGaps : PostedClickGaps);
            return TouchResult.Posted;
        }
        lock (Gate)
        {
            Win32.GetCursorPos(out var before);
            var t = new N.POINTER_TOUCH_INFO();
            t.pointerInfo.pointerType = 2; // touch
            t.pointerInfo.ptPixelLocation = new Win32.POINT { X = screen.X, Y = screen.Y };
            t.touchMask = 7;
            t.orientation = 90;
            t.pressure = 32000;
            t.rcContact = new N.RECT { L = screen.X - 2, T = screen.Y - 2, R = screen.X + 2, B = screen.Y + 2 };
            t.pointerInfo.pointerFlags = N.InRange | N.InContact | N.Down;
            if (!N.InjectTouchInput(1, [t]))
                return TouchResult.Unavailable;
            Thread.Sleep(70);
            t.pointerInfo.pointerFlags = N.InRange | N.InContact | N.Update;
            N.InjectTouchInput(1, [t]);
            Thread.Sleep(25);
            t.pointerInfo.pointerFlags = N.Up;
            N.InjectTouchInput(1, [t]);
            // Windows moves the cursor to the tap a moment later: put it back as soon as it does.
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(15);
                if (Win32.GetCursorPos(out var now) && (now.X != before.X || now.Y != before.Y))
                    Win32.SetCursorPos(before.X, before.Y);
            }
        }
        return TouchResult.Done;
    }

    /// <summary>The cursor stays on the stall's place this long around the posted click (ms: move, down, up).</summary>
    internal static readonly int[] PostedClickGaps = [15, 25, 20];
    /// <summary>The second try, when the first didn't take (the game behind others draws fewer frames).</summary>
    internal static readonly int[] SlowerPostedClickGaps = [40, 60, 40];

    /// <summary>A click posted to the game window (covered by another one): the cursor is put where the
    /// stall is for a moment, as the game reads it from there, then back where the user had it.</summary>
    private static void PostedClick(WindowInfo window, Point client, Point screen, int[] gaps)
    {
        lock (Gate)
        {
            Win32.GetCursorPos(out var before);
            nint at = (client.Y << 16) | (client.X & 0xFFFF);
            try
            {
                Win32.SetCursorPos(screen.X, screen.Y);
                Win32.PostMessage(window.Handle, 0x200, 0, at); // WM_MOUSEMOVE
                Thread.Sleep(gaps[0]);
                Win32.PostMessage(window.Handle, 0x201, 1, at); // WM_LBUTTONDOWN
                Thread.Sleep(gaps[1]);
                Win32.PostMessage(window.Handle, 0x202, 0, at); // WM_LBUTTONUP
                Thread.Sleep(gaps[2]);
            }
            finally
            {
                Win32.SetCursorPos(before.X, before.Y);
            }
        }
    }

    /// <summary>Whether the game itself is seen at this screen point (not minimized, no window over it).</summary>
    public static bool Shows(WindowInfo window, Point screen)
    {
        if (Win32.IsIconic(window.Handle))
            return false;
        nint at = N.WindowFromPoint(new Win32.POINT { X = screen.X, Y = screen.Y });
        return at != 0 && N.GetAncestor(at, 2) == window.Handle;
    }

    private static bool Ready()
    {
        lock (Gate)
            return _ready ??= N.InitializeTouchInjection(1, 1 /* no feedback drawn */);
    }

    private static class N
    {
        public const int InRange = 0x2, InContact = 0x4, Down = 0x10000, Update = 0x20000, Up = 0x40000;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_INFO
        {
            public int pointerType;
            public uint pointerId, frameId;
            public int pointerFlags;
            public nint sourceDevice, hwndTarget;
            public Win32.POINT ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
            public uint dwTime, historyCount;
            public int InputData;
            public uint dwKeyStates;
            public ulong PerformanceCount;
            public int ButtonChangeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_TOUCH_INFO
        {
            public POINTER_INFO pointerInfo;
            public int touchFlags, touchMask;
            public RECT rcContact, rcContactRaw;
            public uint orientation, pressure;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool InitializeTouchInjection(uint maxCount, uint feedbackMode);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool InjectTouchInput(uint count, POINTER_TOUCH_INFO[] contacts);

        [DllImport("user32.dll")]
        public static extern nint WindowFromPoint(Win32.POINT point);

        [DllImport("user32.dll")]
        public static extern nint GetAncestor(nint hwnd, uint flags);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int key);
    }
}
