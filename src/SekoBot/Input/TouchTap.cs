using System.Runtime.InteropServices;
using SekoBot.Capture;
using SekoBot.Native;

namespace SekoBot.Input;

/// <summary>What became of a touch.</summary>
internal enum TouchResult
{
    /// <summary>Touched.</summary>
    Done,
    /// <summary>Another window covers the point (or the game is minimized): not touched.</summary>
    Covered,
    /// <summary>The user kept using the mouse the whole time: not touched.</summary>
    Busy,
    /// <summary>Windows doesn't allow touches from this program.</summary>
    Unavailable,
}

/// <summary>
/// A finger tap on the screen (the game is a mobile game and takes touches): a market stall opens
/// with the game behind other windows, without taking the mouse. Windows puts the cursor on the
/// tapped point for a moment; it is put back at once (about 15 ms). The point must show the game
/// (not covered by another window). Only taps when the user isn't moving the mouse or holding a button.
/// </summary>
internal static class TouchTap
{
    /// <summary>The user's mouse has to be still this long before a tap.</summary>
    internal const int StillMs = 250;
    /// <summary>Waiting for the mouse to be still gives up after this long.</summary>
    internal const int WaitStillMs = 8000;

    private static readonly object Gate = new();
    private static bool? _ready;

    public static async Task<TouchResult> TapAsync(WindowInfo window, Point client, CancellationToken token)
    {
        if (!Ready())
            return TouchResult.Unavailable;
        var screen = WindowService.ClientToScreen(window, client);
        if (!Shows(window, screen))
            return TouchResult.Covered;
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
        if (!Shows(window, screen))
            return TouchResult.Covered;
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
