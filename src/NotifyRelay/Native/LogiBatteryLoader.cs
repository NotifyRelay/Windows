using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace NotifyRelay.Native;

/// <summary>
/// logi-battery DLL 加载器。
/// 仿 NativeCore.Initialize：扫描应用输出目录 → NativeLibrary.Load → 可用性检查。
/// </summary>
public static class LogiBatteryLoader
{
    private static int _initialized = 0; // 0=未初始化, 1=初始化成功, 2=初始化中, -1=初始化失败
    private static string? _loadError;

    public static bool IsAvailable => _initialized == 1;
    public static string? LastError => _loadError;

    /// <summary>
    /// 扫描并加载 logi_battery.dll。线程安全，多次调用仅初始化一次。
    /// </summary>
    public static bool Initialize(ILogger? logger = null)
    {
        if (Interlocked.CompareExchange(ref _initialized, 2, 0) != 0)
        {
            // 另一线程正在初始化或已完成：自旋等待其结束，再返回最终结果，
            // 用专用哨兵 2 区分"初始化中"与"失败(-1)"，避免并发调用误判为失败。
            SpinWait spin = default;
            while (Volatile.Read(ref _initialized) == 2)
                spin.SpinOnce();
            return _initialized == 1;
        }

        try
        {
            var asmLocation = typeof(LogiBatteryNative).Assembly.Location;
            var checkDirs = new List<string>
            {
                // 应用目录相关候选（保留既有行为）
                AppContext.BaseDirectory,
                Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\', '/')) ?? string.Empty,
                Path.GetDirectoryName(asmLocation) ?? string.Empty,
            };
            // 额外兜底：子模块 ffi_test/bin（开发阶段内置 DLL，CI 构建可替换）。
            // 仅当仓库路径为完全限定（绝对）路径时追加，避免将空或相对路径传入
            // Path.Combine/File.Exists/NativeLibrary.Load。
            var repoLogiBatteryPath = GetRepoLogiBatteryPath();
            if (!string.IsNullOrEmpty(repoLogiBatteryPath) &&
                Path.IsPathFullyQualified(repoLogiBatteryPath))
            {
                checkDirs.Add(Path.Combine(repoLogiBatteryPath, "ffi_test", "bin"));
            }

            IntPtr loadedHandle = IntPtr.Zero;
            string? loadedFrom = null;
            foreach (var dir in checkDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var dllPath = Path.Combine(dir, "logi_battery.dll");
                try
                {
                    if (!File.Exists(dllPath)) continue;
                    loadedHandle = NativeLibrary.Load(dllPath);
                    loadedFrom = dllPath;
                    break;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "尝试加载 logi_battery.dll 失败：{Path}", dllPath);
                }
            }

            if (loadedHandle == IntPtr.Zero)
            {
                _loadError = "未找到 logi_battery.dll（已搜索应用目录、子模块 ffi_test/bin）";
                logger?.LogError(_loadError);
                Volatile.Write(ref _initialized, -1);
                return false;
            }

            logger?.LogInformation("logi_battery.dll 加载成功：{Path}", loadedFrom);

            // 冒烟测试：确认导出函数存在（通过 lb_last_error 拿一个指针即可）
            try
            {
                var errPtr = LogiBatteryNative.lb_last_error();
                logger?.LogDebug("logi_battery lb_last_error 导出可达：{Ptr}", errPtr);
            }
            catch (Exception ex)
            {
                _loadError = $"DLL 已加载但导出函数不可达：{ex.Message}";
                logger?.LogError(ex, _loadError);
                Volatile.Write(ref _initialized, -1);
                return false;
            }

            Volatile.Write(ref _initialized, 1);
            return true;
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            logger?.LogError(ex, "LogiBatteryLoader 初始化异常");
            Volatile.Write(ref _initialized, -1);
            return false;
        }
    }

    private static string GetRepoLogiBatteryPath()
    {
        // 约定：NotifyRelay.Overlay/logi-battery 相对于 AppContext.BaseDirectory 的相对位置；
        // 开发环境中可通过 NotifyRelay.sln 解析，这里做两层兜底。
        try
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(dir, "src", "NotifyRelay.Overlay", "logi-battery");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir)!;
                if (string.IsNullOrEmpty(dir)) break;
            }
        }
        catch { /* ignore */ }

        return string.Empty;
    }
}
