using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace SekoBot.UI;

/// <summary>
/// This computer as Windows shows it (name, Windows, processor, memory, graphics card, screen, network cards) next to
/// what the game itself holds about it (read from its memory, only read). Shown on the Gelişmiş page.
/// </summary>
internal static class MachineInfo
{
    /// <summary>"74563c2b285c" → "74:56:3C:2B:28:5C" (empty stays empty).</summary>
    public static string Mac(string raw)
    {
        string hex = new(raw.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12)
            return raw.Trim();
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))).ToUpperInvariant();
    }

    public static string Windows()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string name = key?.GetValue("ProductName") as string ?? "Windows";
            string build = key?.GetValue("CurrentBuildNumber") as string ?? "";
            // Windows 11 still calls itself "Windows 10" here; the build tells.
            if (int.TryParse(build, out int b) && b >= 22000)
                name = name.Replace("Windows 10", "Windows 11");
            string version = key?.GetValue("DisplayVersion") as string ?? "";
            return $"{name} {version} (derleme {build})".Replace("  ", " ").Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return Environment.OSVersion.VersionString; }
    }

    public static string Processor()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string name = (key?.GetValue("ProcessorNameString") as string ?? "").Trim();
            return $"{(name.Length > 0 ? name : "?")} · {Environment.ProcessorCount} çekirdek";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return $"{Environment.ProcessorCount} çekirdek"; }
    }

    public static string Memory()
    {
        long bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return bytes > 0 ? $"{bytes / 1024.0 / 1024 / 1024:0.#} GB" : "?";
    }

    /// <summary>The graphics cards (the display adapters Windows has drivers for).</summary>
    public static string Graphics()
    {
        var names = new List<string>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (string sub in cls?.GetSubKeyNames() ?? [])
            {
                if (!int.TryParse(sub, out _))
                    continue;
                try
                {
                    using var key = cls!.OpenSubKey(sub);
                    if (key?.GetValue("DriverDesc") is string desc && desc.Length > 0 && !names.Contains(desc))
                        names.Add(desc);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        return names.Count > 0 ? string.Join(", ", names) : "?";
    }

    public static string Screens() =>
        string.Join(", ", Screen.AllScreens.Select(s => $"{s.Bounds.Width}×{s.Bounds.Height}" + (s.Primary ? " (ana)" : "")));

    /// <summary>The network cards that are up: name, kind, MAC, the IPv4 addresses on this computer's own network.</summary>
    public static List<(string Name, string Mac, string Ips)> Networks()
    {
        var list = new List<(string, string, string)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                string mac = Mac(nic.GetPhysicalAddress().ToString());
                var ips = nic.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()).ToList();
                // Windows' own filter layers of a card (no address of their own) left out.
                if (ips.Count == 0 || mac.Length == 0)
                    continue;
                list.Add(($"{nic.Name} · {nic.Description}", mac, string.Join(", ", ips)));
            }
        }
        catch (NetworkInformationException) { }
        return list;
    }

    /// <summary>The address the internet (and so the game's server) sees, asked from api.ipify.org (only when the user presses the button).</summary>
    public static async Task<string> PublicIpAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PARDUS");
            return (await http.GetStringAsync("https://api.ipify.org")).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return "alınamadı (" + ex.Message + ")"; }
    }
}

/// <summary>Rows of "what: value", in groups with a caption (the computer's details).</summary>
internal sealed class InfoList : Drawn
{
    public sealed record Row(string Key, string Value, Color? Color = null, bool Caption = false);

    private List<Row> _rows = [];

    public void Show(List<Row> rows)
    {
        _rows = rows;
        Height = (int)(RowHeight * rows.Count + Font.Height * 0.5f);
        Invalidate();
    }

    private float RowHeight => Font.Height * 1.9f;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Clear(g);
        using var caption = Sized(0.82f, FontStyle.Bold);
        using var bold = new Font(Font, FontStyle.Bold);
        int keyW = (int)(Width * 0.27f);
        float y = 0;
        foreach (var row in _rows)
        {
            var r = new Rectangle(0, (int)y, Width, (int)RowHeight);
            if (row.Caption)
            {
                TextRenderer.DrawText(g, row.Key, caption, r with { Y = r.Y + (int)(RowHeight * 0.2f) }, Theme.Faint, LeftMid);
                if (row.Value.Length > 0)
                    TextRenderer.DrawText(g, row.Value, Font, r with { Y = r.Y + (int)(RowHeight * 0.2f) }, Theme.Faint, RightMid);
            }
            else
            {
                using (var line = new Pen(Theme.Border))
                    g.DrawLine(line, 0, r.Bottom - 1, Width, r.Bottom - 1);
                TextRenderer.DrawText(g, row.Key, Font, new Rectangle(0, r.Y, keyW, r.Height), Theme.Muted, LeftMid);
                TextRenderer.DrawText(g, row.Value, bold, new Rectangle(keyW, r.Y, Width - keyW, r.Height), row.Color ?? Theme.Text, LeftMid);
            }
            y += RowHeight;
        }
    }
}
