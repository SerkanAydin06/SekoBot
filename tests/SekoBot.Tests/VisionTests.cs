using SekoBot.Capture;
using SekoBot.Config;
using SekoBot.Vision;
using Xunit;

namespace SekoBot.Tests;

public class VisionTests
{
    /// <summary>200×100 frame with a red bar at y 40..50 filled from x 20 to fillTo.</summary>
    private static Frame BarFrame(int fillTo)
    {
        int w = 200, h = 100;
        var data = new byte[w * h * 4];
        for (int y = 40; y < 50; y++)
        for (int x = 20; x < 180; x++)
        {
            int i = (y * w + x) * 4;
            bool filled = x < fillTo;
            data[i] = filled ? (byte)30 : (byte)25;      // B
            data[i + 1] = filled ? (byte)30 : (byte)25;  // G
            data[i + 2] = filled ? (byte)200 : (byte)40; // R
        }
        return Frame.FromArray(w, h, data);
    }

    [Fact]
    public void Calibrated_bar_reads_its_fill_ratio()
    {
        var area = new Rectangle(20, 40, 160, 10);
        BarSpec spec;
        using (var full = BarFrame(180))
            spec = BarReader.Calibrate(full, area);
        Assert.True(spec.R > 150 && spec.G < 60);

        using (var full = BarFrame(180))
            Assert.InRange(BarReader.Read(full, spec)!.Value, 0.97, 1.0);
        using (var half = BarFrame(100))
            Assert.InRange(BarReader.Read(half, spec)!.Value, 0.45, 0.55);
        using (var empty = BarFrame(20))
            Assert.InRange(BarReader.Read(empty, spec)!.Value, 0.0, 0.03);
    }

    [Fact]
    public void Normalized_rect_round_trips()
    {
        var r = new Rectangle(64, 48, 320, 24);
        var n = NormRect.FromPixels(r, 1024, 768);
        Assert.Equal(r, n.ToPixels(1024, 768));
        Assert.Equal(new Rectangle(128, 96, 640, 48), n.ToPixels(2048, 1536));
    }
}
