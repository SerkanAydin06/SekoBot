using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SekoBot;

/// <summary>Immutable identity + display metadata for a Windows top-level window.</summary>
internal sealed record WindowInfo(nint Handle, uint ProcessId, string Title,
    int Width, int Height, bool IsMinimized)
{
    public bool IsMetin2 =>
        Title.Replace(" ", "", StringComparison.Ordinal)
             .Contains("metin2", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"{Title} · {Width}×{Height} · PID {ProcessId}";
}

/// <summary>Win32 APIs are only used to inspect window identity and the client rectangle.</summary>
internal static class NativeWindows
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X, Y;
    }

    internal delegate bool EnumWindowsCallback(nint window, nint state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    internal static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    internal static extern int GetWindowText(nint window, StringBuilder title, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint window, ref Point position);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    internal static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}

internal static class WindowService
{
    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var found = new List<WindowInfo>();
        NativeWindows.EnumWindowsCallback callback = (handle, _) =>
        {
            if (!NativeWindows.IsWindowVisible(handle))
                return true;

            var length = NativeWindows.GetWindowTextLength(handle);
            if (length < 2)
                return true;

            var title = new StringBuilder(length + 1);
            NativeWindows.GetWindowText(handle, title, title.Capacity);
            var name = title.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
                return true;

            if (!NativeWindows.GetClientRect(handle, out var bounds))
                return true;

            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            if (width < 160 || height < 100)
                return true;

            NativeWindows.GetWindowThreadProcessId(handle, out uint pid);
            if (pid == (uint)Environment.ProcessId)
                return true;

            found.Add(new WindowInfo(
                handle, pid, name, width, height, NativeWindows.IsIconic(handle)));
            return true;
        };

        if (!NativeWindows.EnumWindows(callback, nint.Zero))
            throw new InvalidOperationException("Windows açık pencere listesi okunamadı.");

        return found
            .OrderByDescending(item => item.IsMetin2)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static bool TryGetRegion(WindowInfo target, out Rectangle rectangle, out string error)
    {
        rectangle = Rectangle.Empty;
        error = "";

        if (!NativeWindows.IsWindow(target.Handle))
        {
            error = "Seçilen pencere kapandı. Yeniden tarayın.";
            return false;
        }

        NativeWindows.GetWindowThreadProcessId(target.Handle, out uint currentPid);
        if (currentPid != target.ProcessId)
        {
            error = "Pencere kimliği değişti. Yeniden bağlanın.";
            return false;
        }

        if (NativeWindows.IsIconic(target.Handle))
        {
            error = "Oyun penceresi simge durumunda. Önce geri yükleyin.";
            return false;
        }

        if (!NativeWindows.GetClientRect(target.Handle, out var client))
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

        var origin = new NativeWindows.Point { X = 0, Y = 0 };
        if (!NativeWindows.ClientToScreen(target.Handle, ref origin))
        {
            error = "Oyun penceresinin ekran konumu okunamadı.";
            return false;
        }

        rectangle = new Rectangle(origin.X, origin.Y, width, height);
        return true;
    }
}
