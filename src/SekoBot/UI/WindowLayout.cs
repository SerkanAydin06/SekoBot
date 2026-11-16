using System.Runtime.InteropServices;
using SekoBot.Capture;
using SekoBot.Native;

namespace SekoBot.UI;

/// <summary>
/// Puts several games on one screen tidily: the game windows side by side (a grid for three or
/// more), a band left free under every row for each bot's small bar under its game. Only moves and
/// sizes windows (as dragging them would); nothing in the game is touched. Every SekoBot open is
/// told to turn into its small bar (a window message to all of them).
/// </summary>
internal static class WindowLayout
{
    /// <summary>The message every SekoBot gets: wParam 1 = go small (under your game).</summary>
    public static readonly uint MiniMessage = Win32.RegisterWindowMessage("SekoBot.Mini");

    /// <summary>Tells every SekoBot open (this one too) to go small.</summary>
    public static void TellAllMini() => Win32.PostMessage(Win32.HWND_BROADCAST, MiniMessage, 1, 0);

    /// <summary>The cells of <paramref name="count"/> windows on <paramref name="area"/>: side by side for two, else a grid
    /// (2×2 for three or four, 3×2 for five or six, …). Each row leaves <paramref name="band"/> pixels free under it (the bars).</summary>
    public static List<Rectangle> Grid(Rectangle area, int count, int band = 0)
    {
        var cells = new List<Rectangle>();
        if (count <= 0)
            return cells;
        int columns = count <= 2 ? count : (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling(count / (double)columns);
        for (int i = 0; i < count; i++)
        {
            int c = i % columns, r = i / columns;
            int x0 = area.Left + area.Width * c / columns, x1 = area.Left + area.Width * (c + 1) / columns;
            int y0 = area.Top + area.Height * r / rows, y1 = area.Top + area.Height * (r + 1) / rows - band;
            cells.Add(Rectangle.FromLTRB(x0, y0, x1, y1));
        }
        return cells;
    }

    /// <summary>Where a bot's bar goes: just under its game, else over the game's bottom edge; always on the screen.</summary>
    public static Point BarPlace(Rectangle game, Size bar, Rectangle work)
    {
        int y = game.Bottom + bar.Height <= work.Bottom ? game.Bottom : game.Bottom - bar.Height;
        int x = game.Left;
        return new Point(Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - bar.Width)),
                         Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - bar.Height)));
    }

    /// <summary>The window as it is seen (without the invisible resize borders).</summary>
    public static Rectangle Visible(nint hwnd)
    {
        int size = Marshal.SizeOf<Win32.RECT>();
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, size) != 0 && !Win32.GetWindowRect(hwnd, out r))
            return Rectangle.Empty;
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>Moves a window so that what is seen of it fills <paramref name="target"/>.</summary>
    private static void MoveVisible(nint hwnd, Rectangle target)
    {
        if (Win32.IsIconic(hwnd))
            Win32.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);
        if (Win32.IsZoomed(hwnd))
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        var seen = Visible(hwnd);
        if (!Win32.GetWindowRect(hwnd, out var outer) || seen.IsEmpty)
            return;
        // The invisible borders around the window (Windows 10/11): added back so no gap shows.
        int left = seen.Left - outer.Left, top = seen.Top - outer.Top, right = outer.Right - seen.Right, bottom = outer.Bottom - seen.Bottom;
        Win32.SetWindowPos(hwnd, 0, target.Left - left, target.Top - top, target.Width + left + right, target.Height + top + bottom,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    /// <summary>Arranges every game window on the screen of <paramref name="near"/>, leaving <paramref name="band"/> pixels free under every row.</summary>
    /// <returns>How many game windows were arranged.</returns>
    public static int ArrangeGames(Rectangle near, int band)
    {
        var games = WindowService.GetOpenWindows().Where(w => w.IsMetin2).ToList();
        if (games.Count == 0)
            return 0;
        var work = Screen.FromRectangle(near).WorkingArea;
        var area = work;
        // Keep their order from left to right (then top to bottom), so nothing swaps places.
        var ordered = games.Select(g => (g, at: Visible(g.Handle))).OrderBy(p => p.at.Top / 200).ThenBy(p => p.at.Left).Select(p => p.g).ToList();
        var cells = Grid(area, ordered.Count, band);
        for (int i = 0; i < ordered.Count; i++)
            MoveVisible(ordered[i].Handle, cells[i]);
        return ordered.Count;
    }
}
