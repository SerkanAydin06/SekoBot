using System.Runtime.InteropServices;
using SekoBot.Capture;
using SekoBot.Native;

namespace SekoBot.UI;

/// <summary>
/// Puts several games on one screen tidily: the game windows in a grid, each made smaller in its
/// own shape (never squeezed), a band left free under each game for its bot's small bar. Only moves and
/// sizes windows (as dragging them would); nothing in the game is touched. Every SekoBot open is
/// told to turn into its small bar (a window message to all of them).
/// </summary>
internal static class WindowLayout
{
    /// <summary>The message every SekoBot gets: wParam 1 = go small (under your game).</summary>
    public static readonly uint MiniMessage = Win32.RegisterWindowMessage("SekoBot.Mini");

    /// <summary>Tells every SekoBot open (this one too) to go small.</summary>
    public static void TellAllMini() => Win32.PostMessage(Win32.HWND_BROADCAST, MiniMessage, 1, 0);

    /// <summary>The usual shape of a game picture (width / height), when nothing better is known.</summary>
    public const double DefaultAspect = 16 / 9.0;
    /// <summary>The space left between two games (pixels).</summary>
    public const int Gap = 6;
    private static readonly double[] CommonAspects = [4 / 3.0, 16 / 10.0, 16 / 9.0, 21 / 9.0];

    /// <summary>
    /// Where <paramref name="count"/> games go on <paramref name="area"/>, each kept in its own shape
    /// (<paramref name="aspect"/>, the picture without the title bar: never squeezed or stretched).
    /// The grid that gives the largest games is taken (2 side by side, 3 as two over one, 4 as 2×2,
    /// 6 as 3×2, …), rows centred, the whole centred on the area; under every game
    /// <paramref name="band"/> pixels stay free for its bot's bar.
    /// </summary>
    /// <param name="frame">What the window adds to the picture (title bar, borders), in pixels.</param>
    /// <returns>The windows as they are seen (with their title bars).</returns>
    public static List<Rectangle> Grid(Rectangle area, int count, int band = 0, double aspect = DefaultAspect, Size frame = default, int gap = Gap)
    {
        var cells = new List<Rectangle>();
        if (count <= 0 || area.Width <= 0 || area.Height <= 0)
            return cells;
        if (aspect <= 0)
            aspect = DefaultAspect;
        int columns = 1;
        double width = -1;
        for (int c = 1; c <= count; c++)
        {
            int r = (int)Math.Ceiling(count / (double)c);
            double cellW = (area.Width - gap * (c - 1)) / (double)c - frame.Width;
            double cellH = (area.Height - gap * (r - 1)) / (double)r - band - frame.Height;
            double w = Math.Min(cellW, cellH * aspect);
            if (w > width + 0.5)
            {
                width = w;
                columns = c;
            }
        }
        int rows = (int)Math.Ceiling(count / (double)columns);
        int picW = Math.Max(1, (int)Math.Floor(width)), picH = Math.Max(1, (int)Math.Round(picW / aspect));
        int winW = picW + frame.Width, winH = picH + frame.Height;
        int totalH = rows * (winH + band) + (rows - 1) * gap;
        int top = area.Top + Math.Max(0, (area.Height - totalH) / 2);
        for (int i = 0; i < count; i++)
        {
            int r = i / columns, c = i % columns;
            int inRow = Math.Min(columns, count - r * columns);
            int rowW = inRow * winW + (inRow - 1) * gap;
            int left = area.Left + Math.Max(0, (area.Width - rowW) / 2);
            cells.Add(new Rectangle(left + c * (winW + gap), top + r * (winH + band + gap), winW, winH));
        }
        return cells;
    }

    /// <summary>The shape the games' pictures have: the common shape (4:3, 16:10, 16:9, 21:9) most of them are near,
    /// else 16:9 (a window once squeezed out of shape doesn't count).</summary>
    public static double Aspect(IEnumerable<Size> pictures)
    {
        var near = pictures.Where(p => p.Width > 0 && p.Height > 0)
            .Select(p => CommonAspects.Cast<double?>().FirstOrDefault(a => Math.Abs(p.Width / (double)p.Height / a!.Value - 1) < 0.025))
            .Where(a => a != null).GroupBy(a => a!.Value).OrderByDescending(g => g.Count()).FirstOrDefault();
        return near?.Key ?? DefaultAspect;
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
        // In the bots' order: the 1st SekoBot's game first (top left), then the 2nd, …; games without a
        // SekoBot last, as they stand from left to right.
        var ordered = games.Select(g => (g, at: Visible(g.Handle), bot: Instance.BotOf(g.ProcessId)))
            .OrderBy(p => p.bot).ThenBy(p => p.at.Top / 200).ThenBy(p => p.at.Left).Select(p => p.g).ToList();
        var pictures = ordered.Select(g => Win32.GetClientRect(g.Handle, out var c) ? new Size(c.Right - c.Left, c.Bottom - c.Top) : Size.Empty).ToList();
        // The title bar and borders around the picture (the same for all of them).
        var frame = Size.Empty;
        for (int i = 0; i < ordered.Count && frame.IsEmpty; i++)
        {
            var seen = Visible(ordered[i].Handle);
            if (!seen.IsEmpty && !pictures[i].IsEmpty && !Win32.IsIconic(ordered[i].Handle))
                frame = new Size(Math.Max(0, seen.Width - pictures[i].Width), Math.Max(0, seen.Height - pictures[i].Height));
        }
        var cells = Grid(work, ordered.Count, band, Aspect(pictures), frame);
        for (int i = 0; i < ordered.Count; i++)
            MoveVisible(ordered[i].Handle, cells[i]);
        return ordered.Count;
    }
}
