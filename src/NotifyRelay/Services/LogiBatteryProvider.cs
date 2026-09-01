using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Models.Render;
using NotifyRelay.Native;
using NotifyRelay.Services.Overlay;
using OverlayLogiDevice = NotifyRelay.Models.Render.LogiBatteryDeviceInfo;

namespace NotifyRelay.Services;

/// <summary>
/// 主项目实现的 ILogiBatteryProvider。
/// 负责：调用 Loader 加载 DLL → 轮询/调用 lb_enumerate_devices → 转换为 Overlay 消费模型 LogiBatteryDeviceInfo → 派发 DevicesUpdated 事件。
/// lb_enumerate_devices 每次调用会阻塞（内部启动 tokio runtime），因此放在后台线程轮询。
/// </summary>
public sealed class LogiBatteryProvider : ILogiBatteryProvider, IDisposable
{
    private readonly ILogger<LogiBatteryProvider> _logger;
    private readonly IGeneralSettingsService _settings;

    private IReadOnlyList<OverlayLogiDevice> _devices = Array.Empty<OverlayLogiDevice>();
    private PeriodicTimer? _pollTimer;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>提供给设置页直接绑定（Observable）的副本。</summary>
    public ObservableCollection<OverlayLogiDevice> ObservableDevices { get; } = new();

    public LogiBatteryProvider(ILogger<LogiBatteryProvider> logger, IGeneralSettingsService settings)
    {
        _logger = logger;
        _settings = settings;

        // 首次 Loader 初始化（由 App 启动流程或 DI 时触发，非严格要求提前）
        _ = LogiBatteryLoader.Initialize(logger);
    }

    public IReadOnlyList<OverlayLogiDevice> GetDevices() => Volatile.Read(ref _devices);

    public event EventHandler? DevicesUpdated;

    public void StartMonitoring()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pollTimer != null) return; // 已启动

        if (!LogiBatteryLoader.IsAvailable)
        {
            _logger.LogWarning("LogiBatteryLoader 未就绪，监控未启动。LastError：{Err}", LogiBatteryLoader.LastError);
            return;
        }

        _cts = new CancellationTokenSource();
        _pollTimer = new PeriodicTimer(TimeSpan.FromSeconds(30)); // 30s 刷新一次（电量变化不频繁）
        _ = RunPollingLoopAsync(_cts.Token);
    }

    public void StopMonitoring()
    {
        _cts?.Cancel();
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private async Task RunPollingLoopAsync(CancellationToken ct)
    {
        // 启动时先立即刷新一次
        try { await RefreshOnceAsync(); } catch (Exception ex) { _logger.LogError(ex, "LogiBattery 初始刷新失败"); }

        try
        {
            while (_pollTimer != null && await _pollTimer.WaitForNextTickAsync(ct))
            {
                // 开关关闭时不占用 CPU 去探测 HID
                if (!_settings.LogiBatteryEnabled) continue;
                try
                {
                    await RefreshOnceAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LogiBattery 轮询异常");
                }
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
    }

    private Task RefreshOnceAsync()
    {
        // lb_enumerate_devices 内部阻塞，放到线程池执行
        return Task.Run(() =>
        {
            if (!LogiBatteryLoader.IsAvailable) return;
            int ret = LogiBatteryNative.lb_enumerate_devices(out var list);
            if (ret != 0)
            {
                string? errMsg = null;
                try
                {
                    var errPtr = LogiBatteryNative.lb_last_error();
                    if (errPtr != IntPtr.Zero) errMsg = PtrToStringUtf8(errPtr);
                }
                catch { /* ignore */ }
                _logger.LogWarning("lb_enumerate_devices 返回 {Ret}，错误信息：{Err}", ret, errMsg);
                return;
            }

            var devices = new List<OverlayLogiDevice>(list.count < 0 ? 0 : list.count);
            try
            {
                for (int i = 0; i < list.count; i++)
                {
                    IntPtr itemPtr = IntPtr.Add(list.devices, i * Marshal.SizeOf<LbDeviceInfo>());
                    LbDeviceInfo info = Marshal.PtrToStructure<LbDeviceInfo>(itemPtr);
                    var name = ReadFixedUtf8String(info.name);
                    string deviceId = $"{info.vendor_id:X4}:{info.product_id:X4}:S{info.slot}";
                    int percent = info.has_battery != 0 ? info.percentage : -1;
                    var model = new OverlayLogiDevice
                    {
                        VendorId = info.vendor_id,
                        ProductId = info.product_id,
                        DeviceId = deviceId,
                        DeviceName = string.IsNullOrEmpty(name) ? $"Logitech (slot {info.slot})" : name,
                        Slot = info.slot,
                        Online = info.online != 0,
                        HasBattery = info.has_battery != 0,
                        BatteryPercent = percent,
                        StatusRaw = info.status
                    };
                    devices.Add(model);
                }
            }
            finally
            {
                LogiBatteryNative.lb_free_devices(list);
            }

            Volatile.Write(ref _devices, devices.AsReadOnly());

            // 更新 Observable（在 UI 线程，但此处在 TP，设置页直接在 ViewModel 订阅 DevicesUpdated 后再 DispatcherQueue）
            DevicesUpdated?.Invoke(this, EventArgs.Empty);

            // 同步更新 ObservableDevices（用于设置页直接 x:Bind）
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                ObservableDevices.Clear();
                foreach (var d in devices) ObservableDevices.Add(d);
            });
        });
    }

    private static string ReadFixedUtf8String(byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0) return string.Empty;
        int len = Array.IndexOf(buffer, (byte)0);
        if (len < 0) len = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, len);
    }

    private static unsafe string PtrToStringUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return string.Empty;
        byte* p = (byte*)ptr;
        int len = 0;
        while (p[len] != 0) len++;
        return Encoding.UTF8.GetString(p, len);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
        _cts?.Dispose();
    }
}
