using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SekoBot.Native;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3DBox = Vortice.Mathematics.Box;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace SekoBot.Capture;

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
internal interface IGraphicsCaptureItemInterop
{
    nint CreateForWindow([In] nint window, [In] ref Guid iid);
    nint CreateForMonitor([In] nint monitor, [In] ref Guid iid);
}

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
internal interface IDirect3DDxgiInterfaceAccess
{
    nint GetInterface([In] ref Guid iid);
}

/// <summary>
/// GPU capture through Windows Graphics Capture. Works while the game window is
/// covered by other windows, costs far less CPU than screen copies, and only
/// reads back the client area.
/// </summary>
internal sealed class WgcCapture : ICaptureSource
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Texture2DIid = typeof(ID3D11Texture2D).GUID;

    private readonly WindowInfo _window;
    private readonly long _minFrameTicks;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _staging;
    private int _stagingWidth, _stagingHeight;
    private SizeInt32 _poolSize;
    private ChannelWriter<Frame>? _output;
    private long _lastFrameTicks = -(1L << 50);
    private long _sequence;
    private Rectangle _clientInCapture;
    private long _clientCheckedTicks = -(1L << 50);
    private bool _failed, _disposed;

    public WgcCapture(WindowInfo window, int maxFps)
    {
        _window = window;
        _minFrameTicks = Stopwatch.Frequency / Math.Clamp(maxFps, 1, 120);
    }

    public static bool IsSupported
    {
        get
        {
            try { return GraphicsCaptureSession.IsSupported(); }
            catch { return false; }
        }
    }

    public string Name => "Windows Graphics Capture (GPU)";
    public event Action<string>? Failed;

    public void Initialize()
    {
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            levels, out _device).CheckError();
        _context = _device!.ImmediateContext;

        using (var dxgi = _device.QueryInterface<IDXGIDevice>())
        {
            Marshal.ThrowExceptionForHR(Win32.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out nint inspectable));
            try { _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
            finally { Marshal.Release(inspectable); }
        }

        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemIid;
        nint itemPointer = interop.CreateForWindow(_window.Handle, ref iid);
        try { _item = GraphicsCaptureItem.FromAbi(itemPointer); }
        finally { Marshal.Release(itemPointer); }

        _item.Closed += (_, _) => Fail("Oyun penceresi kapandı.");
        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _session = _pool.CreateCaptureSession(_item);

        const string sessionType = "Windows.Graphics.Capture.GraphicsCaptureSession";
        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(sessionType, "IsCursorCaptureEnabled"))
                _session.IsCursorCaptureEnabled = false;
        }
        catch { }
        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(sessionType, "IsBorderRequired"))
                _session.IsBorderRequired = false;
        }
        catch { }
    }

    public void Start(ChannelWriter<Frame> output)
    {
        if (_session == null || _pool == null)
            throw new InvalidOperationException("WGC başlatılmadı.");
        _output = output;
        _pool.FrameArrived += OnFrameArrived;
        _session.StartCapture();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            try
            {
                using var captured = sender.TryGetNextFrame();
                if (captured == null)
                    return;

                long now = _clock.ElapsedTicks;
                if (now - _lastFrameTicks < _minFrameTicks)
                    return;

                var size = captured.ContentSize;
                if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
                {
                    _poolSize = size;
                    sender.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                    _clientCheckedTicks = -(1L << 50);
                    return;
                }

                // The client offset only changes when the window is resized or restyled.
                if (now - _clientCheckedTicks > Stopwatch.Frequency / 2)
                {
                    if (!WindowService.IsAlive(_window, out var error))
                    {
                        Fail(error);
                        return;
                    }
                    if (WindowService.TryGetClientRectInCapture(_window, out var client))
                        _clientInCapture = client;
                    else
                        _clientInCapture = new Rectangle(0, 0, size.Width, size.Height);
                    _clientCheckedTicks = now;
                }

                var crop = Rectangle.Intersect(_clientInCapture, new Rectangle(0, 0, size.Width, size.Height));
                if (crop.Width < 32 || crop.Height < 32)
                    return;

                var access = captured.Surface.As<IDirect3DDxgiInterfaceAccess>();
                var textureIid = Texture2DIid;
                using var texture = new ID3D11Texture2D(access.GetInterface(ref textureIid));

                EnsureStaging(crop.Width, crop.Height);
                _context!.CopySubresourceRegion(_staging!, 0, 0, 0, 0, texture, 0,
                    new D3DBox(crop.Left, crop.Top, 0, crop.Right, crop.Bottom, 1));

                var mapped = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                Frame frame;
                try
                {
                    frame = Frame.Rent(crop.Width, crop.Height, ++_sequence);
                    int rowBytes = crop.Width * 4;
                    for (int y = 0; y < crop.Height; y++)
                        Marshal.Copy(mapped.DataPointer + (nint)(y * (long)mapped.RowPitch), frame.Data, y * frame.Stride, rowBytes);
                }
                finally
                {
                    _context.Unmap(_staging!, 0);
                }

                _lastFrameTicks = now;
                if (_output == null || !_output.TryWrite(frame))
                    frame.Dispose();
            }
            catch (Exception ex)
            {
                Fail("GPU yakalama hatası: " + ex.Message);
            }
        }
    }

    private void EnsureStaging(int width, int height)
    {
        if (_staging != null && _stagingWidth == width && _stagingHeight == height)
            return;
        _staging?.Dispose();
        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };
        _staging = _device!.CreateTexture2D(description);
        _stagingWidth = width;
        _stagingHeight = height;
    }

    private void Fail(string message)
    {
        if (_failed)
            return;
        _failed = true;
        ThreadPool.QueueUserWorkItem(_ => Failed?.Invoke(message));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_pool != null)
                _pool.FrameArrived -= OnFrameArrived;
            try { _session?.Dispose(); } catch { }
            try { _pool?.Dispose(); } catch { }
            _staging?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
        }
    }
}
