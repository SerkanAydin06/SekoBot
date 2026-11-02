using System.Drawing;
using System.Drawing.Imaging;

namespace SekoBot;

internal sealed record CaptureResult(Bitmap? Frame, string Error);

/// <summary>
/// Reads visible client-area pixels. No injection, memory inspection or keyboard commands.
/// This backend cannot capture minimized/occluded DirectX game windows.
/// </summary>
internal static class ScreenCapture
{
    public static CaptureResult Grab(WindowInfo window)
    {
        if (!WindowService.TryGetRegion(window, out Rectangle bounds, out string error))
            return new CaptureResult(null, error);

        Bitmap? image = null;
        try
        {
            image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.CopyFromScreen(
                    bounds.Location, Point.Empty, bounds.Size,
                    CopyPixelOperation.SourceCopy);
            }

            Bitmap finished = image;
            image = null;
            return new CaptureResult(finished, "");
        }
        catch (Exception ex)
        {
            return new CaptureResult(null, $"Görüntü yakalama başarısız: {ex.Message}");
        }
        finally
        {
            image?.Dispose();
        }
    }
}
