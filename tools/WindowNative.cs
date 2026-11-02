// C# 5-compatible Win32 interop, compiled in-memory by built-in Windows PowerShell.
// This is NOT a Godot C# script: no Godot .NET SDK or external Python installation.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SekoBot
{
    public class WindowRecord
    {
        public long hwnd { get; set; }
        public long pid { get; set; }
        public string title { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public bool minimized { get; set; }
        public bool metin2 { get; set; }
    }

    public static class WindowNative
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

        public delegate bool EnumerateCallback(IntPtr hwnd, IntPtr state);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumerateCallback callback, IntPtr state);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLengthW(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder title, int maxCount);
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
        private static extern bool SetDpiContext(IntPtr context);
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static void InitializeDpi()
        {
            try
            {
                if (SetDpiContext(new IntPtr(-4)))
                    return;
            }
            catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); }
            catch (Exception) { }
        }

        public static WindowRecord[] ListWindows()
        {
            List<WindowRecord> found = new List<WindowRecord>();
            EnumerateCallback callback = delegate(IntPtr hwnd, IntPtr state)
            {
                try
                {
                    if (!IsWindowVisible(hwnd))
                        return true;
                    int length = GetWindowTextLengthW(hwnd);
                    if (length < 2)
                        return true;
                    StringBuilder text = new StringBuilder(length + 1);
                    GetWindowTextW(hwnd, text, text.Capacity);
                    string title = text.ToString().Trim();
                    if (title.Length == 0)
                        return true;
                    RECT rect;
                    if (!GetClientRect(hwnd, out rect))
                        return true;
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    if (width < 300 || height < 200)
                        return true;
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    found.Add(new WindowRecord {
                        hwnd = hwnd.ToInt64(),
                        pid = pid,
                        title = title,
                        width = width,
                        height = height,
                        minimized = IsIconic(hwnd),
                        metin2 = title.Replace(" ", "").IndexOf("metin2",
                            StringComparison.OrdinalIgnoreCase) >= 0
                    });
                }
                catch (Exception) { /* Ignore windows that close during enumeration. */ }
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            found.Sort(delegate(WindowRecord a, WindowRecord b)
            {
                int priority = b.metin2.CompareTo(a.metin2);
                return priority != 0 ? priority :
                    StringComparer.OrdinalIgnoreCase.Compare(a.title, b.title);
            });
            return found.ToArray();
        }

        public static string Capture(long handle, long expectedPid, string targetFile)
        {
            try
            {
                IntPtr hwnd = new IntPtr(handle);
                if (!IsWindow(hwnd))
                    return "Oyun penceresi kapatıldı veya HWND geçersiz.";
                uint actualPid;
                GetWindowThreadProcessId(hwnd, out actualPid);
                if (actualPid != (uint)expectedPid)
                    return "Oyun penceresinin işlem kimliği değişti; yeniden bağlanın.";
                if (IsIconic(hwnd))
                    return "Oyun simge durumunda. Pencereyi geri yükleyin.";

                RECT rect;
                if (!GetClientRect(hwnd, out rect))
                    return "Oyun penceresinin boyutu okunamadı.";
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width < 32 || height < 32 || width > 7680 || height > 4320)
                    return "Oyun penceresinin boyutu geçersiz.";
                POINT origin = new POINT { X = 0, Y = 0 };
                if (!ClientToScreen(hwnd, ref origin))
                    return "Oyun penceresinin ekran konumu okunamadı.";

                // Captures visible desktop pixels; occluded/minimized windows cannot be read.
                using (Bitmap image = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics graphics = Graphics.FromImage(image))
                    {
                        graphics.CopyFromScreen(origin.X, origin.Y, 0, 0,
                            new Size(width, height), CopyPixelOperation.SourceCopy);
                    }
                    string temporaryFile = targetFile + ".partial";
                    image.Save(temporaryFile, ImageFormat.Png);
                    File.Move(temporaryFile, targetFile);
                }
                return "";
            }
            catch (Exception ex)
            {
                return "Ekran yakalama hatası: " + ex.Message;
            }
        }
    }
}
