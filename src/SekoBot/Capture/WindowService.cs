using System.Text;
using SekoBot.Native;

namespace SekoBot.Capture;

/// <summary>Immutable identity of a top-level window.</summary>
internal sealed record WindowInfo(nint Handle, uint ProcessId, string Title,
    int Width, int Height, bool IsMinimized)
{
    public bool IsMetin2 =>
        Title.Replace(" ", "", StringComparison.Ordinal)
             .Contains("metin2", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Title} · {Width}×{Height} · PID {ProcessId}";
}

internal static class WindowService
{
    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var found = new List<WindowInfo>();
        uint self = (uint)Environment.ProcessId;
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd))
                return true;
            int length = Win32.GetWindowTextLength(hwnd);
            if (length < 2)
                return true;
            var text = new StringBuilder(length + 1);
            Win32.GetWindowText(hwnd, text, text.Capacity);
            string title = text.ToString().Trim();
            if (title.Length == 0 || !Win32.GetClientRect(hwnd, out var client))
                return true;
            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            bool minimized = Win32.IsIconic(hwnd);
            if (!minimized && (width < 160 || height < 100))
                return true;
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self)
                return true;
            found.Add(new WindowInfo(hwnd, pid, title, width, height, minimized));
            return true;
        }, 0);

        return found
            .OrderByDescending(w => w.IsMetin2)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Checks that the HWND still belongs to the same process.</summary>
    public static bool IsAlive(WindowInfo target, out string error)
    {
        error = "";
        if (!Win32.IsWindow(target.Handle))
        {
            error = "Seçilen pencere kapandı. Yeniden tarayın.";
            return false;
        }
        Win32.GetWindowThreadProcessId(target.Handle, out uint pid);
        if (pid != target.ProcessId)
        {
            error = "Pencere kimliği değişti. Yeniden bağlanın.";
            return false;
        }
        return true;
    }

    /// <summary>Client area in screen pixels.</summary>
    public static bool TryGetClientScreenRect(WindowInfo target, out Rectangle rect, out string error)
    {
        rect = Rectangle.Empty;
        if (!IsAlive(target, out error))
            return false;
        if (Win32.IsIconic(target.Handle))
        {
            error = "Oyun penceresi simge durumunda. Önce geri yükleyin.";
            return false;
        }
        if (!Win32.GetClientRect(target.Handle, out var client))
        {
            error = "Oyun penceresinin görüntü alanı okunamadı.";
            return false;
        }
        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        if (width < 32 || height < 32 || width > 7680 || height > 4320)
        {
            error = "Oyun penceresinin görüntü alanı geçersiz.";
            return false;
        }
        var origin = new Win32.POINT();
        if (!Win32.ClientToScreen(target.Handle, ref origin))
        {
            error = "Oyun penceresinin ekran konumu okunamadı.";
            return false;
        }
        rect = new Rectangle(origin.X, origin.Y, width, height);
        return true;
    }

    /// <summary>
    /// Windows Graphics Capture returns the whole visible window frame
    /// (DWM extended frame bounds). This returns where the client area sits inside it.
    /// </summary>
    public static bool TryGetClientRectInCapture(WindowInfo target, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!TryGetClientScreenRect(target, out var client, out _))
            return false;
        Win32.RECT frame;
        int size = System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>();
        if (Win32.DwmGetWindowAttribute(target.Handle, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out frame, size) != 0 &&
            !Win32.GetWindowRect(target.Handle, out frame))
            return false;
        rect = new Rectangle(client.X - frame.Left, client.Y - frame.Top, client.Width, client.Height);
        return true;
    }

    public static Point ClientToScreen(WindowInfo target, Point client)
    {
        var p = new Win32.POINT { X = client.X, Y = client.Y };
        Win32.ClientToScreen(target.Handle, ref p);
        return new Point(p.X, p.Y);
    }
}
