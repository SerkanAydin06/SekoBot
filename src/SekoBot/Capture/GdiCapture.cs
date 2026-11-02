using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace SekoBot.Capture;

/// <summary>
/// Fallback: copies visible screen pixels. Needs the game to be visible and unobstructed.
/// One bitmap is reused for the whole session.
/// </summary>
internal sealed class GdiCapture : ICaptureSource
{
    private readonly WindowInfo _window;
    private readonly int _maxFps;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public GdiCapture(WindowInfo window, int maxFps)
    {
        _window = window;
        _maxFps = Math.Clamp(maxFps, 1, 60);
    }

    public string Name => "GDI (ekran kopyası)";
    public event Action<string>? Failed;

    public void Start(ChannelWriter<Frame> output) =>
        _loop = Task.Run(() => Run(output, _cts.Token));

    private async Task Run(ChannelWriter<Frame> output, CancellationToken token)
    {
        Bitmap? buffer = null;
        long sequence = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var step = Stopwatch.StartNew();
                if (!WindowService.TryGetClientScreenRect(_window, out var area, out var error))
                {
                    Failed?.Invoke(error);
                    return;
                }
                if (buffer == null || buffer.Width != area.Width || buffer.Height != area.Height)
                {
                    buffer?.Dispose();
                    buffer = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
                }
                using (var g = Graphics.FromImage(buffer))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);

                var frame = Frame.Rent(area.Width, area.Height, ++sequence);
                var bits = buffer.LockBits(new Rectangle(0, 0, area.Width, area.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    for (int y = 0; y < area.Height; y++)
                        Marshal.Copy(bits.Scan0 + y * bits.Stride, frame.Data, y * frame.Stride, frame.Stride);
                }
                finally
                {
                    buffer.UnlockBits(bits);
                }
                if (!output.TryWrite(frame))
                    frame.Dispose();

                int wait = (int)(1000.0 / _maxFps - step.Elapsed.TotalMilliseconds);
                await Task.Delay(Math.Max(1, wait), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Failed?.Invoke("GDI yakalama hatası: " + ex.Message);
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { }
        _cts.Dispose();
    }
}
