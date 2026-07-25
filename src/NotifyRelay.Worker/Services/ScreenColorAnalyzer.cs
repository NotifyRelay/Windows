using System.Runtime.InteropServices;
using Windows.UI;
using Microsoft.Extensions.Logging;

namespace NotifyRelay.Worker.Services;

public sealed class ScreenColorAnalyzer : IDisposable
{
    private readonly ILogger? _logger;
    private Thread? _captureThread;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private int _screenWidth;
    private int _screenHeight;

    private const int CaptureWidth = 160;
    private const int CaptureHeight = 90;

    private Color _currentCapturedColor = new() { A = 255, R = 0, G = 0, B = 0 };

    public event EventHandler<Color>? ColorChanged;

    public bool IsCapturing => _captureThread?.IsAlive ?? false;

    public bool IsCaptureSupported
    {
        get
        {
            try
            {
                var dc = GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;
                ReleaseDC(IntPtr.Zero, dc);
                return true;
            }
            catch { return false; }
        }
    }

    public Color CurrentCapturedColor => _currentCapturedColor;

    public ScreenColorAnalyzer(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void StartCapture()
    {
        if (IsCapturing) return;

        _screenWidth = GetSystemMetrics(SM_CXSCREEN);
        _screenHeight = GetSystemMetrics(SM_CYSCREEN);

        if (_screenWidth <= 0 || _screenHeight <= 0) return;

        _cts = new CancellationTokenSource();
        _captureThread = new Thread(CaptureLoop)
        {
            Name = "GDI Capture",
            IsBackground = true
        };
        _captureThread.Start();

        _logger?.LogInformation("GDI capture started: {W}x{H} (sample: {SW}x{SH})",
            _screenWidth, _screenHeight, CaptureWidth, CaptureHeight);
    }

    public void StopCapture()
    {
        if (_captureThread == null) return;

        _cts?.Cancel();
        _captureThread?.Join(TimeSpan.FromSeconds(2));
        _captureThread = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void CaptureLoop()
    {
        var token = _cts!.Token;
        GdiCaptureBuffer? buffer = null;

        while (!token.IsCancellationRequested)
        {
            try
            {
                buffer ??= new GdiCaptureBuffer(_logger);

                if (!buffer.Capture(_screenWidth, _screenHeight, out var color))
                {
                    Thread.Sleep(100);
                    continue;
                }

                _currentCapturedColor = color;
                ColorChanged?.Invoke(this, color);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in capture loop");
                Thread.Sleep(100);
            }
        }

        buffer?.Dispose();
    }

    private sealed class GdiCaptureBuffer : IDisposable
    {
        private readonly ILogger? _logger;
        private IntPtr _hdcMem = IntPtr.Zero;
        private IntPtr _hBitmap = IntPtr.Zero;
        private IntPtr _oldBitmap = IntPtr.Zero;
        private byte[] _pixelBuffer = new byte[CaptureWidth * CaptureHeight * 4];

        public GdiCaptureBuffer(ILogger? logger) => _logger = logger;

        public bool Capture(int screenWidth, int screenHeight, out Color color)
        {
            color = default;
            var hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return false;

            try
            {
                if (_hdcMem == IntPtr.Zero)
                {
                    _hdcMem = CreateCompatibleDC(hdcScreen);
                    if (_hdcMem == IntPtr.Zero) return false;
                }

                if (_hBitmap == IntPtr.Zero)
                {
                    _hBitmap = CreateCompatibleBitmap(hdcScreen, CaptureWidth, CaptureHeight);
                    if (_hBitmap == IntPtr.Zero) return false;
                    _oldBitmap = SelectObject(_hdcMem, _hBitmap);
                }

                if (!StretchBlt(_hdcMem, 0, 0, CaptureWidth, CaptureHeight,
                                hdcScreen, 0, 0, screenWidth, screenHeight, SRCCOPY))
                    return false;

                var bmpInfo = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = CaptureWidth,
                    biHeight = -CaptureHeight,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = 0
                };

                var gcHandle = GCHandle.Alloc(_pixelBuffer, GCHandleType.Pinned);
                try
                {
                    var result = GetDIBits(hdcScreen, _hBitmap, 0, (uint)CaptureHeight,
                                           gcHandle.AddrOfPinnedObject(), ref bmpInfo, DIB_RGB_COLORS);
                    if (result == 0) return false;

                    color = CalculatePredominantColor(_pixelBuffer);
                    return true;
                }
                finally { gcHandle.Free(); }
            }
            finally { ReleaseDC(IntPtr.Zero, hdcScreen); }
        }

        private static Color CalculatePredominantColor(byte[] pixels)
        {
            int count = pixels.Length / 4;
            if (count == 0) return new Color { A = 255, R = 0, G = 0, B = 0 };

            const int bins = 8;
            const int valuesPerBin = 32;
            const float threshold = 0.3f;
            const float alpha = 0.4f;
            const float binDisplacement = 0.01f;

            Span<int> histR = stackalloc int[bins];
            Span<int> histG = stackalloc int[bins];
            Span<int> histB = stackalloc int[bins];

            for (int i = 0; i < count; i++)
            {
                int offset = i * 4;
                histB[pixels[offset] / valuesPerBin]++;
                histG[pixels[offset + 1] / valuesPerBin]++;
                histR[pixels[offset + 2] / valuesPerBin]++;
            }

            float avgR = 0, avgG = 0, avgB = 0;
            for (int bin = 0; bin < bins; bin++)
            {
                float w = (float)bin / count;
                avgR += histR[bin] * w;
                avgG += histG[bin] * w;
                avgB += histB[bin] * w;
            }
            byte avgRbyte = (byte)Math.Min(avgR * valuesPerBin, 255);
            byte avgGbyte = (byte)Math.Min(avgG * valuesPerBin, 255);
            byte avgBbyte = (byte)Math.Min(avgB * valuesPerBin, 255);

            float topR = 0, topG = 0, topB = 0;
            int topRCount = 0, topGCount = 0, topBCount = 0;
            for (int bin = bins - 1; bin >= 0; bin--)
            {
                if ((float)topRCount / count < threshold)
                {
                    topR += histR[bin] * (bin + binDisplacement);
                    topRCount += histR[bin];
                }
                if ((float)topGCount / count < threshold)
                {
                    topG += histG[bin] * (bin + binDisplacement);
                    topGCount += histG[bin];
                }
                if ((float)topBCount / count < threshold)
                {
                    topB += histB[bin] * (bin + binDisplacement);
                    topBCount += histB[bin];
                }
            }
            byte topRbyte = topRCount > 0 ? (byte)Math.Min(topR / topRCount * valuesPerBin, 255) : (byte)0;
            byte topGbyte = topGCount > 0 ? (byte)Math.Min(topG / topGCount * valuesPerBin, 255) : (byte)0;
            byte topBbyte = topBCount > 0 ? (byte)Math.Min(topB / topBCount * valuesPerBin, 255) : (byte)0;

            return new Color
            {
                A = 255,
                R = (byte)((1 - alpha) * topRbyte + alpha * avgRbyte),
                G = (byte)((1 - alpha) * topGbyte + alpha * avgGbyte),
                B = (byte)((1 - alpha) * topBbyte + alpha * avgBbyte)
            };
        }

        public void Dispose()
        {
            if (_oldBitmap != IntPtr.Zero && _hdcMem != IntPtr.Zero)
                SelectObject(_hdcMem, _oldBitmap);
            if (_hBitmap != IntPtr.Zero) DeleteObject(_hBitmap);
            if (_hdcMem != IntPtr.Zero) DeleteDC(_hdcMem);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCapture();
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint startScan, uint numScans, IntPtr bits, ref BITMAPINFOHEADER bi, uint colorUse);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes;
        public ushort biBitCount; public uint biCompression; public uint biSizeImage;
        public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant;
    }
}
