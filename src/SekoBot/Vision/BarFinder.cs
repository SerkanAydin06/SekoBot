using SekoBot.Capture;
using SekoBot.Config;

namespace SekoBot.Vision;

internal enum BarColor { Red = 1, Blue = 2 }

/// <summary>A thin, solid, horizontal coloured strip found on screen.</summary>
internal readonly record struct BarCandidate(Rectangle Area, BarColor Color, double Density);

internal sealed record BarDetection(Rectangle? Hp, Rectangle? Mp, Rectangle? Target, IReadOnlyList<BarCandidate> Candidates);

/// <summary>
/// Finds the HP (red) and MP (blue) bars without user calibration: they are the most solid
/// thin horizontal red/blue strips in the lower part of the screen. A red strip near the top
/// centre is the selected target's HP bar. Only the filled part is visible, so the result is
/// as wide as the bar is full; <see cref="AutoBars"/> widens it later when the bar fills up.
/// </summary>
internal static class BarFinder
{
    public static BarDetection Find(Frame frame)
    {
        int w = frame.Width, h = frame.Height;
        int minRun = Math.Max(20, w / 50);
        var candidates = new List<BarCandidate>();
        // Bars are thin, so every row is scanned, but only where bars can be.
        Scan(frame, 0, (int)(h * 0.30), minRun, candidates);
        Scan(frame, (int)(h * 0.50), h, minRun, candidates);

        BarCandidate? hp = candidates
            .Where(c => c.Color == BarColor.Red && Center(c).Y > h * 0.55)
            .OrderByDescending(Score).Cast<BarCandidate?>().FirstOrDefault();

        BarCandidate? mp = candidates
            .Where(c => c.Color == BarColor.Blue && Center(c).Y > h * 0.55)
            .OrderByDescending(c => Score(c) * (hp is { } r && NearHp(c.Area, r.Area, w, h) ? 3 : 1))
            .Cast<BarCandidate?>().FirstOrDefault();

        BarCandidate? target = candidates
            .Where(c => c.Color == BarColor.Red && Center(c).Y < h * 0.30 && Math.Abs(Center(c).X - w / 2.0) < w * 0.25)
            .OrderByDescending(Score).Cast<BarCandidate?>().FirstOrDefault();

        return new BarDetection(hp?.Area, mp?.Area, target?.Area, candidates);
    }

    private static PointF Center(BarCandidate c) => new(c.Area.X + c.Area.Width / 2f, c.Area.Y + c.Area.Height / 2f);

    private static double Score(BarCandidate c) => c.Area.Width * c.Density * Math.Min(c.Area.Height, 10);

    /// <summary>Metin2 stacks the MP bar right next to the HP bar.</summary>
    private static bool NearHp(Rectangle mp, Rectangle hp, int w, int h) =>
        Math.Abs(mp.Left - hp.Left) < w * 0.06 && Math.Abs(mp.Top - hp.Top) < h * 0.08;

    /// <summary>0 = nothing, 1 = red, 2 = blue. Needs a bright, clearly coloured pixel.</summary>
    internal static int Classify(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max < 90 || (max - min) * 255 < 110 * max)
            return 0;
        if (r == max && g * 100 < r * 55 && b * 100 < r * 55)
            return 1;
        if (b == max && r * 100 < b * 60 && g * 100 < b * 90)
            return 2;
        return 0;
    }

    private sealed class Blob
    {
        public int Left, Right, Top, Bottom, Hits, LastRow;
        public int Color;
    }

    private static void Scan(Frame frame, int fromY, int toY, int minRun, List<BarCandidate> output)
    {
        int w = frame.Width;
        var data = frame.Data;
        var open = new List<Blob>();
        var next = new List<Blob>();
        for (int y = fromY; y < toY; y++)
        {
            next.Clear();
            int row = y * frame.Stride;
            int x = 0;
            while (x < w)
            {
                int i = row + x * 4;
                int c = Classify(data[i + 2], data[i + 1], data[i]);
                if (c == 0) { x++; continue; }
                // Horizontal run of colour c, tolerating gaps of up to 2 px (gradients, ticks).
                int start = x, end = x, hits = 0, gap = 0;
                for (; x < w; x++)
                {
                    int j = row + x * 4;
                    if (Classify(data[j + 2], data[j + 1], data[j]) == c) { hits++; end = x; gap = 0; }
                    else if (++gap > 2) break;
                }
                int length = end - start + 1;
                if (length < minRun)
                    continue;
                var blob = open.FirstOrDefault(b => b.Color == c && b.LastRow == y - 1 &&
                    Math.Abs(b.Left - start) <= 3 && Math.Abs(b.Right - end) <= 3 && !next.Contains(b));
                if (blob == null)
                    blob = new Blob { Left = start, Right = end, Top = y, Color = c };
                blob.Left = Math.Min(blob.Left, start);
                blob.Right = Math.Max(blob.Right, end);
                blob.Bottom = y;
                blob.LastRow = y;
                blob.Hits += hits;
                next.Add(blob);
            }
            foreach (var b in open)
                if (!next.Contains(b)) Close(b, output);
            open.Clear();
            open.AddRange(next);
        }
        foreach (var b in open)
            Close(b, output);
    }

    private static void Close(Blob b, List<BarCandidate> output)
    {
        int width = b.Right - b.Left + 1, height = b.Bottom - b.Top + 1;
        if (height < 2 || height > 24 || width < 4 * height)
            return;
        double density = (double)b.Hits / (width * height);
        if (density < 0.6)
            return;
        output.Add(new BarCandidate(new Rectangle(b.Left, b.Top, width, height), (BarColor)b.Color, density));
    }
}

/// <summary>
/// Keeps the profile's automatically found bars up to date. Manually marked bars
/// (<see cref="BarSpec.Auto"/> false) are never touched.
/// </summary>
internal sealed class AutoBars
{
    private const int RelocateAfterMisses = 10;
    private readonly Dictionary<string, int> _misses = [];
    private readonly List<NormRect> _rejectedTargets = [];

    /// <summary>Never take this area as the target bar again (it turned out not to be one).</summary>
    public void Reject(Rectangle area, int width, int height) =>
        _rejectedTargets.Add(NormRect.FromPixels(area, width, height));

    private bool IsRejected(Rectangle area, Frame frame) =>
        _rejectedTargets.Any(r => r.ToPixels(frame.Width, frame.Height).IntersectsWith(area));

    /// <returns>Messages for the log; empty when nothing changed.</returns>
    public List<string> Apply(BotProfile profile, BarDetection found, Frame frame)
    {
        var messages = new List<string>();
        var target = found.Target is Rectangle t && IsRejected(t, frame) ? null : found.Target;
        profile.TargetBar = Update(profile.TargetBar, target, frame, "Hedef HP", messages);
        return messages;
    }

    private BarSpec? Update(BarSpec? current, Rectangle? found, Frame frame, string name, List<string> messages)
    {
        if (current is { Auto: false })
            return current;
        if (found is not Rectangle area)
            return current;
        if (current == null)
        {
            messages.Add($"{name} barı otomatik bulundu ✓");
            return Calibrate(frame, area);
        }

        var known = current.Area.ToPixels(frame.Width, frame.Height);
        bool sameBar = Math.Abs(known.Top - area.Top) <= 3 && Math.Abs(known.Left - area.Left) <= 4;
        if (sameBar)
        {
            _misses[name] = 0;
            // Only the filled part is visible: grow when the bar is fuller than before, never shrink.
            if (area.Right > known.Right + 2)
                return Calibrate(frame, Rectangle.FromLTRB(known.Left, Math.Min(known.Top, area.Top),
                    area.Right, Math.Max(known.Bottom, area.Bottom)));
            return current;
        }
        // A bar somewhere else, consistently, while ours reads empty: the UI moved (resize, new resolution).
        int misses = _misses.GetValueOrDefault(name) + 1;
        _misses[name] = misses;
        if (misses >= RelocateAfterMisses && (BarReader.Read(frame, current) ?? 0) < 0.05)
        {
            _misses[name] = 0;
            messages.Add($"{name} barı yeni yerinde bulundu ✓");
            return Calibrate(frame, area);
        }
        return current;
    }

    private static BarSpec Calibrate(Frame frame, Rectangle area)
    {
        var spec = BarReader.Calibrate(frame, area);
        spec.Auto = true;
        return spec;
    }
}
