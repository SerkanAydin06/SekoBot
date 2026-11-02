using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SekoBot.Capture;

namespace SekoBot.Recording;

/// <summary>
/// On-disk layout of one recording folder:
///   info.json    – app version, start time, frame size
///   profile.json – profile (calibration + timings) in effect when recording started
///   ticks.jsonl  – one line per brain tick: bar readings, phase, actions, optional frame file
///   frames/      – PNG frames referenced from ticks.jsonl
/// JSON lines keep a recording readable even if the app crashed mid-session.
/// </summary>
internal static class RecordingFormat
{
    public const int Version = 1;
    public const string InfoFile = "info.json";
    public const string ProfileFile = "profile.json";
    public const string TicksFile = "ticks.jsonl";
    public const string FramesDir = "frames";

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static readonly JsonSerializerOptions JsonIndented = new(Json) { WriteIndented = true };

    public static string Root { get; } = Path.Combine(Config.ProfileStore.Directory, "recordings");

    public static void SavePng(Frame frame, string path)
    {
        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppRgb);
        var bits = bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < frame.Height; y++)
                Marshal.Copy(frame.Data, y * frame.Stride, bits.Scan0 + y * bits.Stride, frame.Stride);
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    public static Frame LoadPng(string path, long sequence = 0)
    {
        using var bitmap = new Bitmap(path);
        int w = bitmap.Width, h = bitmap.Height;
        var data = new byte[w * h * 4];
        var bits = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(bits.Scan0 + y * bits.Stride, data, y * w * 4, w * 4);
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
        return Frame.FromArray(w, h, data, sequence);
    }
}

internal sealed class RecordingInfo
{
    public int Version { get; set; } = RecordingFormat.Version;
    public string AppVersion { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public string Window { get; set; } = "";
}

/// <summary>One brain tick. <see cref="Frame"/> is null when no image was stored for it.</summary>
internal sealed class RecordedTick
{
    public double T { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public double? Hp { get; set; }
    public double? Mp { get; set; }
    public double? Target { get; set; }
    public string? Phase { get; set; }
    public List<string>? Actions { get; set; }
    public string? Frame { get; set; }

    public TimeSpan Time => TimeSpan.FromMilliseconds(T);
}
