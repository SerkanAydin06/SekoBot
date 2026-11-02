// SekoBot: isolated Windows capture adapter. No code injection, memory access or input sending.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Godot;
using GodotArray = Godot.Collections.Array<Godot.Collections.Dictionary>;
using GodotDictionary = Godot.Collections.Dictionary;

public partial class WindowCaptureBridge : Node
{
    private string _lastError = "";

    // These two entry points intentionally return only Godot-compatible types.
    public GodotArray ListWindows()
    {
        var result = new GodotArray();
        _lastError = "";
        if (!OperatingSystem.IsWindows())
        {
            _lastError = "Pencere algılama şu anda yalnızca Windows üzerinde destekleniyor.";
            return result;
        }

        try
        {
            var found = new List<WindowRecord>();
            Native.EnumWindows((hwnd, _) =>
            {
                if (!Native.IsWindowVisible(hwnd))
                    return true;

                var titleLength = Native.GetWindowTextLengthW(hwnd);
                if (titleLength <= 1)
                    return true;

                var buffer = new StringBuilder(titleLength + 1);
                Native.GetWindowTextW(hwnd, buffer, buffer.Capacity);
                var title = buffer.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title))
                    return true;

                if (!Native.GetClientRect(hwnd, out var rect))
                    return true;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width < 300 || height < 200)
                    return true;

                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                found.Add(new WindowRecord(hwnd, title, pid, width, height, Native.IsIconic(hwnd)));
                return true;
            }, IntPtr.Zero);

            foreach (var win in found.OrderByDescending(w => IsMetin2(w.Title))
                         .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new GodotDictionary
                {
                    ["hwnd"] = win.Handle.ToInt64(),
                    ["pid"] = (long)win.ProcessId,
                    ["title"] = win.Title,
                    ["width"] = win.Width,
                    ["height"] = win.Height,
                    ["minimized"] = win.Minimized,
                    ["metin2"] = IsMetin2(win.Title)
                });
            }
        }
        catch (Exception ex)
        {
            _lastError = $"Pencere listesi okunamadı: {ex.Message}";
        }
        return result;
    }

    public string GetLastError() => _lastError;

    public Image? CaptureFrame(long windowHandle, long expectedPid)
    {
        _lastError = "";
        if (!OperatingSystem.IsWindows())
        {
            _lastError = "Görüntü yakalama yalnızca Windows'ta destekleniyor.";
            return null;
        }

        var hwnd = new IntPtr(windowHandle);
        if (!Native.IsWindow(hwnd))
        {
            _lastError = "Oyun penceresi kapandı veya artık bulunamıyor.";
            return null;
        }

        Native.GetWindowThreadProcessId(hwnd, out uint actualPid);
        if (actualPid != (uint)expectedPid)
        {
            _lastError = "Pencere kimliği değişti; yeniden bağlanmalısınız.";
            return null;
        }

        if (Native.IsIconic(hwnd))
        {
            _lastError = "Oyun simge durumunda. Pencereyi geri yükleyin.";
            return null;
        }

        if (!Native.GetClientRect(hwnd, out var rect))
        {
            _lastError = "Oyun alanının boyutu alınamadı.";
            return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 32 || height < 32 || width > 7680 || height > 4320)
        {
            _lastError = "Geçersiz oyun alanı boyutu.";
            return null;
        }

        var origin = new Native.POINT { X = 0, Y = 0 };
        if (!Native.ClientToScreen(hwnd, ref origin))
        {
            _lastError = "Oyun alanının ekran konumu alınamadı.";
            return null;
        }

        // BitBlt reads the *visible desktop*, so the game must not be occluded/minimized.
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;
        try
        {
            screenDc = Native.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                throw new InvalidOperationException("Masaüstü görüntüsü açılamadı.");

            memoryDc = Native.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
                throw new InvalidOperationException("Görüntü tamponu oluşturulamadı.");

            var info = new Native.BITMAPINFO
            {
                Header = new Native.BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                    Width = width,
                    Height = -height, // top-down DIB
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = (uint)(width * height * 4)
                }
            };

            bitmap = Native.CreateDIBSection(
                screenDc, ref info, 0, out IntPtr bitmapPixels, IntPtr.Zero, 0
            );
            if (bitmap == IntPtr.Zero || bitmapPixels == IntPtr.Zero)
                throw new InvalidOperationException("DIB görüntü belleği oluşturulamadı.");

            previousBitmap = Native.SelectObject(memoryDc, bitmap);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
                throw new InvalidOperationException("Görüntü tamponu seçilemedi.");

            if (!Native.BitBlt(memoryDc, 0, 0, width, height,
                    screenDc, origin.X, origin.Y, 0x00CC0020))
                throw new InvalidOperationException("Ekran yakalama başarısız.");

            Native.GdiFlush();
            byte[] rgba = new byte[checked(width * height * 4)];
            Marshal.Copy(bitmapPixels, rgba, 0, rgba.Length);
            for (int i = 0; i < rgba.Length; i += 4)
            {
                (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]); // BGRA -> RGBA
                rgba[i + 3] = 255;
            }
            return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        }
        catch (Exception ex)
        {
            _lastError = $"Ekran görüntüsü alınamadı: {ex.Message}";
            return null;
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1))
                Native.SelectObject(memoryDc, previousBitmap);
            if (bitmap != IntPtr.Zero)
                Native.DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                Native.DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero)
                Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static bool IsMetin2(string title) =>
        title.Replace(" ", "", StringComparison.Ordinal)
             .Contains("metin2", StringComparison.OrdinalIgnoreCase);

    private record WindowRecord(IntPtr Handle, string Title, uint ProcessId, int Width, int Height, bool Minimized);

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER Header;
            public uint Colors;
        }

        public delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
        public static extern int GetWindowTextLengthW(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
        public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder title, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hwnd, ref POINT origin);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(
            IntPtr hdc, ref BITMAPINFO info, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(
            IntPtr destinationDc, int x, int y, int width, int height,
            IntPtr sourceDc, int sourceX, int sourceY, uint rasterOperation);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool GdiFlush();
    }
}
