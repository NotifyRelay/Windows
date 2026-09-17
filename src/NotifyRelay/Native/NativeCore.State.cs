namespace NotifyRelay.Native;

public static partial class NativeCore
{
    public static void UpdateHeartbeatSchedulerParams(string name, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.UpdateHeartbeatSchedulerParams(_ctx, name, battery, deviceType);
    }

    // ======== Device state snapshot ========
    // 传 0 表示使用 Rust core 内建阈值（已认证 12s / 未认证 20s）：
    // 在线判定与可见性完全归 core，两端平台保持一致
    public static string? GetDeviceList(long authedTimeoutMs = 0, long unauthedTimeoutMs = 0)
    {
        return NotifyRelayCore.Safe.GetDeviceList(_ctx, authedTimeoutMs, unauthedTimeoutMs);
    }

    // ======== Sender queue ========
    public static long SenderQueueHandle => _senderQueueHandle;

    public static void EnqueueMessage(string deviceUuid, string header, string plaintext, string? dedupKey = null)
    {
        NotifyRelayCore.Safe.EnqueueMessage(_ctx, _senderQueueHandle, deviceUuid, header, plaintext, dedupKey);
    }

    // 推送「全量」超级岛/媒体状态；Rust 内部 diff 并经 on_data 回调回传合并后的全量。
    public static void PushSuperIslandState(string deviceUuid, string fullJson, bool isEnd = false)
    {
        NotifyRelayCore.Safe.PushSuperIslandState(_ctx, _senderQueueHandle, deviceUuid, fullJson, isEnd);
    }

    public static void PushMediaState(string deviceUuid, string fullJson, bool isEnd = false)
    {
        NotifyRelayCore.Safe.PushMediaState(_ctx, _senderQueueHandle, deviceUuid, fullJson, isEnd);
    }
}
