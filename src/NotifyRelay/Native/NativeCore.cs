using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Services.Devices;
using NotifyRelay.Services.Media;
using NotifyRelay.Services.Protocol;
using NotifyRelay.Services.Infrastructure;

namespace NotifyRelay.Native;

public static partial class NativeCore
{
    private static IntPtr _ctx = IntPtr.Zero;
    private static bool _initialized = false;
    private static string? _gitHash;

    // 回调分发目标
    internal static ProtocolRouter? ProtocolRouter { get; set; }
    internal static IDeviceManager? DeviceManager { get; set; }
    internal static NetworkService? NetworkService { get; set; }
    internal static HeartbeatProcessor? HeartbeatProcessor { get; set; }

    // 媒体会话存在性查询委托（由 WindowsPlaybackService 注册）：返回 true=仍有活跃媒体会话，false=无
    // Rust 心跳查询回调（on_state_query）调用，运行在 Rust 心跳线程；无活跃会话时 Rust 移除媒体发送会话。
    public static Func<string, bool>? MediaSessionQueryHandler { get; set; }

    // 保持回调委托不被 GC 回收
    private static readonly List<Delegate> _callbackRefs = new();

    // 已上线的设备集合（仅用于控制"设备已连接"日志仅状态变化时打印；维持性心跳连接不上线下线不打印）
    private static readonly ConcurrentDictionary<string, byte> _deviceOnline = new();

    public static IntPtr Context => _ctx;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        var asmLocation = typeof(NotifyRelayCore).Assembly.Location;
        var checkDirs = new[] {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\')),
            Path.GetDirectoryName(asmLocation)
        };

        foreach (var dir in checkDirs)
        {
            if (dir == null) continue;
            var dllPath = Path.Combine(dir, "notify_relay_core.dll");
            if (File.Exists(dllPath))
            {
                NativeLibrary.Load(dllPath);
                break;
            }
        }

        _ctx = NotifyRelayCore.nrc_init();
        _gitHash = GetGitHash();
    }

    public static string? GetGitHash()
    {
        var ptr = NotifyRelayCore.nrc_get_git_hash();
        if (ptr == IntPtr.Zero) return null;
        var result = Marshal.PtrToStringAnsi(ptr);
        NotifyRelayCore.nrc_free_string(ptr);
        return result;
    }

    // ======== Heartbeat scheduler ========
    private static long _senderQueueHandle;
}
