using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace NotifyRelay.DeviceCtrl.DynamicLighting;

public class ScreenColorAnalyzer
{
    private GraphicsCaptureSession? _session;
    private Direct3D11CaptureFramePool? _framePool;
    private bool _isCapturing;
    private readonly ILogger<ScreenColorAnalyzer>? _logger;
    private Color _currentCapturedColor = new() { A = 255, R = 0, G = 0, B = 0 };

    public event EventHandler<Color>? ColorChanged;

    public bool IsCaptureSupported => GraphicsCaptureSession.IsSupported();

    public bool IsCapturing => _isCapturing;

    public Color CurrentCapturedColor => _currentCapturedColor;

    public async Task StartCaptureAsync()
    {
        if (_isCapturing)
            return;

        if (!IsCaptureSupported)
            return;

        try
        {
            var monitorHandle = NotifyRelay.Platforms.Windows.Helpers.Win32Helper.GetPrimaryMonitorHandle();
            if (monitorHandle == IntPtr.Zero)
            {
                _logger?.LogError("Failed to get primary monitor handle");
                return;
            }

            var item = CreateCaptureItemForMonitor(monitorHandle);
            if (item == null)
            {
                _logger?.LogError("Failed to create capture item for monitor");
                return;
            }

            var d3dDevice = CreateDirect3DDevice();
            if (d3dDevice == null)
            {
                _logger?.LogError("Failed to create Direct3D device");
                return;
            }
            
            _framePool = Direct3D11CaptureFramePool.Create(
                d3dDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);

            _session = _framePool.CreateCaptureSession(item);
            _session.IsBorderRequired = false;

            _framePool.FrameArrived += FramePool_FrameArrived;

            _session.StartCapture();
            _isCapturing = true;
            _logger?.LogInformation("Screen capture started");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start screen capture");
        }
    }

    public void StopCapture()
    {
        if (!_isCapturing)
            return;

        _framePool?.FrameArrived -= FramePool_FrameArrived;
        _framePool?.Dispose();
        _framePool = null;

        _session?.Dispose();
        _session = null;

        _isCapturing = false;
        _logger?.LogInformation("Screen capture stopped");
    }

    private async void FramePool_FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null)
                return;

            var screenColor = await AnalyzeFrameAsync(frame);
            _currentCapturedColor = screenColor;
            ColorChanged?.Invoke(this, screenColor);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing captured frame");
        }
    }

    private async Task<Color> AnalyzeFrameAsync(Direct3D11CaptureFrame frame)
    {
        var width = (int)frame.ContentSize.Width;
        var height = (int)frame.ContentSize.Height;

        using var frameBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
        using var convertedBitmap = SoftwareBitmap.Convert(frameBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);

        var pixelBuffer = convertedBitmap.LockBuffer(BitmapBufferAccessMode.Read);
        var stride = pixelBuffer.GetPlaneDescription(0).Stride;

        using var reference = pixelBuffer.CreateReference();
        
        byte[] pixelData;
        ((IMemoryBufferByteAccess)reference).GetBuffer(out var data, out var length);
        pixelData = new byte[length];
        Marshal.Copy(data, pixelData, 0, (int)length);

        return CalculateDominantColor(pixelData, width, height, stride);
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }

    private Color CalculateDominantColor(byte[] pixelData, int width, int height, int stride)
    {
        long rSum = 0, gSum = 0, bSum = 0;
        int pixelCount = 0;

        int sampleStep = Math.Max(1, width * height / 10000);

        for (int y = 0; y < height; y += sampleStep)
        {
            for (int x = 0; x < width; x += sampleStep)
            {
                int index = y * stride + x * 4;
                if (index + 3 < pixelData.Length)
                {
                    bSum += pixelData[index];
                    gSum += pixelData[index + 1];
                    rSum += pixelData[index + 2];
                    pixelCount++;
                }
            }
        }

        if (pixelCount == 0)
            return new Color { A = 255, R = 0, G = 0, B = 0 };

        byte r = (byte)(rSum / pixelCount);
        byte g = (byte)(gSum / pixelCount);
        byte b = (byte)(bSum / pixelCount);

        return new Color { A = 255, R = r, G = g, B = b };
    }

    private GraphicsCaptureItem? CreateCaptureItemForMonitor(IntPtr monitorHandle)
    {
        try
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            Guid guid = typeof(GraphicsCaptureItem).GUID;
            interop.CreateForMonitor(monitorHandle, ref guid, out object item);
            return item as GraphicsCaptureItem;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create capture item for monitor");
            return null;
        }
    }

    private Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? CreateDirect3DDevice()
    {
        try
        {
            int hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null,
                0,
                D3D11_SDK_VERSION,
                out var pDevice,
                IntPtr.Zero,
                IntPtr.Zero);

            if (hr != 0 || pDevice == IntPtr.Zero)
            {
                hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                    IntPtr.Zero,
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                    null,
                    0,
                    D3D11_SDK_VERSION,
                    out pDevice,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }

            if (hr != 0 || pDevice == IntPtr.Zero)
                return null;

            object? d3dDevice = null;
            hr = CreateDirect3D11DeviceFromDXGIDevice(pDevice, ref d3dDevice);

            Marshal.Release(pDevice);

            if (hr != 0 || d3dDevice == null)
                return null;

            return d3dDevice as Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create Direct3D device");
            return null;
        }
    }

    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x200;
    private const int D3D11_SDK_VERSION = 7;

    private enum D3D_DRIVER_TYPE
    {
        D3D_DRIVER_TYPE_HARDWARE = 0,
        D3D_DRIVER_TYPE_REFERENCE = 1,
        D3D_DRIVER_TYPE_NULL = 2,
        D3D_DRIVER_TYPE_SOFTWARE = 3,
        D3D_DRIVER_TYPE_WARP = 5
    }

    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", PreserveSig = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        D3D_DRIVER_TYPE DriverType,
        IntPtr Software,
        uint Flags,
        IntPtr[]? pFeatureLevels,
        uint FeatureLevels,
        int SDKVersion,
        out IntPtr ppDevice,
        IntPtr pFeatureLevel,
        IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        ref object? graphicsDevice);

    [ComImport]
    [Guid("3628e81b-3c7c-4a09-a8f5-7d90794116dd")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        void CreateForMonitor([In] IntPtr monitorHandle, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.IUnknown)] out object graphicsCaptureItem);
    }

    public ScreenColorAnalyzer(ILogger<ScreenColorAnalyzer>? logger = null)
    {
        _logger = logger;
    }
}
