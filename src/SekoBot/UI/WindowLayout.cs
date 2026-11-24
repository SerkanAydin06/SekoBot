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

    /// <summary>The message every SekoBot gets for all bots at once: wParam <see cref="AllStart"/>, <see cref="AllPause"/> or <see cref="AllStop"/>.</summary>
    public static readonly uint AllMessage = Win32.RegisterWindowMessage("SekoBot.All");
    public const int AllStart = 1, AllPause = 2, AllStop = 3, AllUpdate = 4;

    /// <summary>Tells every SekoBot open (this one too) to start, pause or stop its work (each in its own tab).</summary>
    public static void TellAll(int what) => Win32.PostMessage(Win32.HWND_BROADCAST, AllMessage, what, 0);

    /// <summary>The menu for all bots at once (the small bar's and the header's button).</summary>
    public static ContextMenuStrip AllMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("▶  Tüm botları başlat / sürdür", null, (_, _) => TellAll(AllStart));
        menu.Items.Add("⏸  Tüm botları duraklat", null, (_, _) => TellAll(AllPause));
        menu.Items.Add("■  Tüm botları durdur", null, (_, _) => TellAll(AllStop));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("⬆  Tüm PARDUS'ları güncelle (kaldığı yerden devam)", null, (_, _) => TellAll(AllUpdate));
        return menu;
    }

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

    /// <summary>The narrowest picture of a game in the whole-screens grid (4:3 at 480 high).</summary>
    public const int UiWholeMinWidth = 640;

    /// <summary>
    /// The games laid out so their screens show whole: every picture <see cref="GameUiMinHeight"/> high and as wide
    /// as the columns allow (at least 4:3), side by side without gaps, <paramref name="band"/> free under each row.
    /// Null when they don't fit so.
    /// </summary>
    public static List<Rectangle>? UiWholeGrid(Rectangle area, int count, int band, Size frame = default)
    {
        if (count <= 0)
            return null;
        int columns = Math.Min(count, Math.Max(1, area.Width / (UiWholeMinWidth + frame.Width)));
        int rows = (int)Math.Ceiling(count / (double)columns);
        int winH = GameUiMinHeight + frame.Height;
        if (rows * (winH + band) > area.Height)
            return null;
        int winW = area.Width / columns;
        int top = area.Top + (area.Height - rows * (winH + band)) / 2;
        var cells = new List<Rectangle>();
        for (int i = 0; i < count; i++)
        {
            int r = i / columns, c = i % columns;
            int inRow = Math.Min(columns, count - r * columns);
            int left = area.Left + (area.Width - inRow * winW) / 2;
            cells.Add(new Rectangle(left + c * winW, top + r * (winH + band), winW, winH));
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

    /// <summary>
    /// One game made big: its screen's whole work area but a band at the bottom for its bar (the bar stays
    /// under the game, never over it). Already big: false (the caller arranges them all again).
    /// </summary>
    public static bool FillWithGame(nint hwnd, int band)
    {
        if (!Win32.IsWindow(hwnd))
            return false;
        var seen = Visible(hwnd);
        var work = Screen.FromRectangle(seen.IsEmpty ? Rectangle.Empty : seen).WorkingArea;
        var target = new Rectangle(work.Left, work.Top, work.Width, work.Height - band);
        if (Math.Abs(seen.Width - target.Width) < 12 && Math.Abs(seen.Height - target.Height) < 12 && Math.Abs(seen.Left - target.Left) < 12)
            return false;
        MoveVisible(hwnd, target);
        Win32.SetForegroundWindow(hwnd);
        return true;
    }

    /// <summary>
    /// The game's own rule (measured 2026-11-20): its screens get smaller with the window only while the
    /// picture's short side is at least 480 pixels; below that they stay as big and are cut off at the
    /// edges (the login button drops off the bottom of a 358-pixel-high window).
    /// </summary>
    public const int GameUiMinHeight = 480;

    /// <summary>
    /// Where a game goes when it must show its screens whole: in its own shape, the picture just
    /// <see cref="GameUiMinHeight"/> high, grown from where it stands and kept on the work area.
    /// </summary>
    /// <param name="seen">The window as it is seen now.</param>
    /// <param name="picture">Its picture (the client area) now.</param>
    /// <returns>The window as it should be seen; <paramref name="seen"/> when it is already big enough.</returns>
    public static Rectangle UiWhole(Rectangle seen, Size picture, Rectangle work)
    {
        if (picture.Width <= 0 || picture.Height <= 0 || Math.Min(picture.Width, picture.Height) >= GameUiMinHeight)
            return seen;
        double aspect = picture.Width / (double)picture.Height;
        int picH = aspect >= 1 ? GameUiMinHeight : (int)Math.Round(GameUiMinHeight / aspect);
        int picW = aspect >= 1 ? (int)Math.Round(GameUiMinHeight * aspect) : GameUiMinHeight;
        int w = Math.Min(work.Width, picW + seen.Width - picture.Width), h = Math.Min(work.Height, picH + seen.Height - picture.Height);
        int x = Math.Clamp(seen.Left, work.Left, work.Right - w), y = Math.Clamp(seen.Top, work.Top, work.Bottom - h);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>A game grown just enough to show its screens whole (see <see cref="UiWhole"/>), brought to the front.</summary>
    /// <returns>Where it now is; empty when it was big enough already.</returns>
    public static Rectangle GrowForUi(nint hwnd)
    {
        if (!Win32.IsWindow(hwnd) || !Win32.GetClientRect(hwnd, out var c))
            return Rectangle.Empty;
        var seen = Visible(hwnd);
        var target = UiWhole(seen, new Size(c.Right - c.Left, c.Bottom - c.Top), Screen.FromRectangle(seen).WorkingArea);
        if (target == seen)
            return Rectangle.Empty;
        MoveVisible(hwnd, target);
        Win32.SetForegroundWindow(hwnd);
        return target;
    }

    /// <summary>Puts a game back where it was (what is seen of it).</summary>
    public static void PutBack(nint hwnd, Rectangle seen)
    {
        if (Win32.IsWindow(hwnd) && !seen.IsEmpty)
            MoveVisible(hwnd, seen);
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
        // Smaller than the game's screens need (cut off: the bag's bottom out of the window, seen): the games
        // 480 high side by side, without gaps, when they fit so (6 on 1920×1032: 640×480 each, a slim bar under each).
        if (cells.Count > 0 && cells[0].Height - frame.Height < GameUiMinHeight && UiWholeGrid(work, ordered.Count, band, frame) is { } whole)
            cells = whole;
        for (int i = 0; i < ordered.Count; i++)
            MoveVisible(ordered[i].Handle, cells[i]);
        return ordered.Count;
    }
}
