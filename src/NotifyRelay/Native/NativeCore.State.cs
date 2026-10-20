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

    /// <returns>1=已入队 0=命中去重丢弃 -1=参数无效或队列未初始化</returns>
    public static int EnqueueMessage(string deviceUuid, string header, string plaintext)
    {
        return NotifyRelayCore.Safe.EnqueueMessage(_ctx, _senderQueueHandle, deviceUuid, header, plaintext);
    }

    /// <summary>入站内容去重（10s 窗口）：action 0=纯查询（1=已见/0=未见）1=标记已见 2=清除全部内容键</summary>
    public static int ContentDedup(int action, string title, string text)
    {
        return NotifyRelayCore.Safe.ContentDedup(_ctx, action, title, text);
    }

    public static void PushMediaState(string deviceUuid, string fullJson, bool isEnd = false)
    {
        NotifyRelayCore.Safe.PushMediaState(_ctx, _senderQueueHandle, deviceUuid, fullJson, isEnd);
    }
}
