using SekoBot.Capture;
using SekoBot.Config;
using Cv = OpenCvSharp;

namespace SekoBot.Vision;

/// <summary>
/// Finds Metin stones by multi-scale template matching on a half-resolution
/// greyscale copy of the search area. Templates are cut from the live preview
/// by the user, so they match the server's look and the user's graphics settings.
/// </summary>
internal sealed class StoneDetector : IDisposable
{
    private const double WorkScale = 0.5;
    private const int MaxPerTemplate = 4;

    private readonly object _gate = new();
    private List<Cv.Mat> _templates = [];
    private List<(Cv.Mat Mat, double Scale)> _scaled = [];
    private double[] _scales = [];

    public int TemplateCount
    {
        get { lock (_gate) return _templates.Count; }
    }

    public void LoadFrom(string directory, double[] scales)
    {
        var loaded = new List<Cv.Mat>();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.png").Order())
            {
                try
                {
                    var mat = Cv.Cv2.ImDecode(File.ReadAllBytes(file), Cv.ImreadModes.Grayscale);
                    if (mat.Empty()) { mat.Dispose(); continue; }
                    loaded.Add(mat);
                }
                catch { }
            }
        }
        lock (_gate)
        {
            DisposeAll();
            _templates = loaded;
            _scales = scales.Length == 0 ? [1.0] : scales;
            foreach (var template in _templates)
            foreach (var scale in _scales)
            {
                double factor = WorkScale * scale;
                int w = Math.Max(6, (int)Math.Round(template.Width * factor));
                int h = Math.Max(6, (int)Math.Round(template.Height * factor));
                var resized = new Cv.Mat();
                Cv.Cv2.Resize(template, resized, new Cv.Size(w, h), 0, 0, Cv.InterpolationFlags.Area);
                _scaled.Add((resized, scale));
            }
        }
    }

    /// <summary>Saves a crop of the frame as a new template PNG.</summary>
    public static string SaveTemplate(Frame frame, Rectangle area, string directory)
    {
        area = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
        if (area.Width < 12 || area.Height < 12)
            throw new ArgumentException("Şablon için en az 12×12 piksellik bir alan seçin.");
        Directory.CreateDirectory(directory);
        byte[] png = WithMat(frame, source =>
        {
            using var crop = new Cv.Mat(source, new Cv.Rect(area.X, area.Y, area.Width, area.Height));
            using var gray = new Cv.Mat();
            Cv.Cv2.CvtColor(crop, gray, Cv.ColorConversionCodes.BGRA2GRAY);
            Cv.Cv2.ImEncode(".png", gray, out byte[] encoded);
            return encoded;
        });
        string path = Path.Combine(directory, $"metin_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        File.WriteAllBytes(path, png);
        return path;
    }

    public IReadOnlyList<Detection> Detect(Frame frame, Rectangle searchArea, double threshold)
    {
        searchArea = Rectangle.Intersect(searchArea, new Rectangle(0, 0, frame.Width, frame.Height));
        lock (_gate)
        {
            if (_scaled.Count == 0 || searchArea.Width < 32 || searchArea.Height < 32)
                return [];

            var area = searchArea;
            using var small = WithMat(frame, source =>
            {
                using var roi = new Cv.Mat(source, new Cv.Rect(area.X, area.Y, area.Width, area.Height));
                using var gray = new Cv.Mat();
                Cv.Cv2.CvtColor(roi, gray, Cv.ColorConversionCodes.BGRA2GRAY);
                var resized = new Cv.Mat();
                Cv.Cv2.Resize(gray, resized, new Cv.Size(), WorkScale, WorkScale, Cv.InterpolationFlags.Area);
                return resized;
            });

            var hits = new List<Detection>();
            using var result = new Cv.Mat();
            foreach (var (template, _) in _scaled)
            {
                if (template.Width >= small.Width || template.Height >= small.Height)
                    continue;
                Cv.Cv2.MatchTemplate(small, template, result, Cv.TemplateMatchModes.CCoeffNormed);
                for (int i = 0; i < MaxPerTemplate; i++)
                {
                    Cv.Cv2.MinMaxLoc(result, out _, out double max, out _, out Cv.Point at);
                    if (max < threshold)
                        break;
                    var box = new Rectangle(
                        searchArea.X + (int)(at.X / WorkScale),
                        searchArea.Y + (int)(at.Y / WorkScale),
                        (int)(template.Width / WorkScale),
                        (int)(template.Height / WorkScale));
                    hits.Add(new Detection(box, max));
                    // Suppress this peak so the next MinMaxLoc finds another stone.
                    int sx = Math.Max(0, at.X - template.Width / 2);
                    int sy = Math.Max(0, at.Y - template.Height / 2);
                    int sw = Math.Min(result.Width - sx, template.Width);
                    int sh = Math.Min(result.Height - sy, template.Height);
                    if (sw > 0 && sh > 0)
                        using (var region = new Cv.Mat(result, new Cv.Rect(sx, sy, sw, sh)))
                            region.SetTo(new Cv.Scalar(-1));
                }
            }
            return NonMaxSuppression(hits, 0.3);
        }
    }

    internal static IReadOnlyList<Detection> NonMaxSuppression(List<Detection> hits, double maxOverlap)
    {
        var kept = new List<Detection>();
        foreach (var hit in hits.OrderByDescending(h => h.Score))
        {
            if (kept.All(k => IoU(k.Box, hit.Box) <= maxOverlap))
                kept.Add(hit);
        }
        return kept;
    }

    internal static double IoU(Rectangle a, Rectangle b)
    {
        var i = Rectangle.Intersect(a, b);
        if (i.IsEmpty)
            return 0;
        double inter = (double)i.Width * i.Height;
        return inter / ((double)a.Width * a.Height + (double)b.Width * b.Height - inter);
    }

    private static unsafe T WithMat<T>(Frame frame, Func<Cv.Mat, T> work)
    {
        fixed (byte* pixels = frame.Data)
        {
            using var mat = Cv.Mat.FromPixelData(frame.Height, frame.Width, Cv.MatType.CV_8UC4,
                (nint)pixels, frame.Stride);
            return work(mat);
        }
    }

    private void DisposeAll()
    {
        foreach (var t in _templates) t.Dispose();
        foreach (var (m, _) in _scaled) m.Dispose();
        _templates = [];
        _scaled = [];
    }

    public void Dispose()
    {
        lock (_gate) DisposeAll();
    }
}
