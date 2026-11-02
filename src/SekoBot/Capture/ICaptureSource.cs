using System.Threading.Channels;

namespace SekoBot.Capture;

internal interface ICaptureSource : IDisposable
{
    /// <summary>Shown in the UI, e.g. "Windows Graphics Capture".</summary>
    string Name { get; }

    /// <summary>Raised once when capture can no longer continue.</summary>
    event Action<string>? Failed;

    /// <summary>Starts producing frames. Ownership of each frame passes to the channel.</summary>
    void Start(ChannelWriter<Frame> output);
}

internal static class CaptureFactory
{
    public static ICaptureSource Create(WindowInfo window, int maxFps, Action<string> log)
    {
        if (WgcCapture.IsSupported)
        {
            var wgc = new WgcCapture(window, maxFps);
            try
            {
                wgc.Initialize();
                return wgc;
            }
            catch (Exception ex)
            {
                wgc.Dispose();
                log("Windows Graphics Capture başlatılamadı, GDI'ya geçiliyor: " + ex.Message);
            }
        }
        return new GdiCapture(window, maxFps);
    }
}
