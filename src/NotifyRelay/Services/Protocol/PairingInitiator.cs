using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services.Protocol;

/// <summary>
/// PC 作为配对发起端的编排。
///
/// 协议流程与 Android 发起端一致，全部由 core 完成：
/// 生成配对码 → 发送 PAIRING_INIT（内含 SPAKE2 prover）→ 等待对端回 PAIRING_RESP →
/// 回传 ACCEPT 并建立设备密钥；结果经 core 的 <c>on_pairing</c> RESULT 回调上抛。
/// </summary>
public sealed class PairingInitiator(ILogger<PairingInitiator> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>结果等待器：targetUuid → 完成源（1=成功，0=失败）。</summary>
    private readonly Dictionary<string, TaskCompletionSource<bool>> waiters = [];

    /// <summary>最近一次失败原因（targetUuid → 原因），供 UI 细化提示。</summary>
    private readonly Dictionary<string, string> failureReasons = [];

    /// <summary>
    /// 发起配对：**先返回配对码**，发送在后台进行。
    ///
    /// core 的 <c>nrc_send_pairing_init</c> 是同步阻塞调用（发出 PAIRING_INIT 后同步等待对端回
    /// PAIRING_RESP，读超时 60s），而配对码必须先展示给用户、由对端输入后才会有响应，
    /// 因此该调用不能挡在配对码展示之前，否则必然直到超时才返回、界面始终不显示配对码。
    /// </summary>
    /// <returns>
    /// 配对码与配对结果 Task；配对码为 null 表示生成失败。
    /// 结果 Task 在成功/失败/取消时完成，失败原因经 <see cref="FailureReason"/> 读取。
    /// </returns>
    public async Task<(string? Code, Task<bool> Completion)> BeginAsync(
        string targetUuid,
        int battery,
        string deviceType,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var code = NativeCore.GeneratePairingCode();
            if (string.IsNullOrEmpty(code))
            {
                logger.LogError("生成配对码失败：core 未初始化");
                return (null, Task.FromResult(false));
            }

            var localUuid = NativeCore.GetLocalUuid() ?? string.Empty;
            if (string.IsNullOrEmpty(localUuid))
            {
                logger.LogError("发起配对失败：本机 uuid 为空");
                NativeCore.ClearPairingCode();
                return (null, Task.FromResult(false));
            }

            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (waiters)
            {
                waiters[targetUuid] = waiter;
                failureReasons.Remove(targetUuid);
            }

            // 后台阻塞调用：发出 PAIRING_INIT 并同步等待对端响应；本方法立即返回配对码
            _ = Task.Run(
                () =>
                {
                    try
                    {
                        var rc = NativeCore.SendPairingInit(localUuid, targetUuid, code, battery, deviceType);
                        if (rc != 0)
                        {
                            logger.LogWarning("发起配对未成功发送: target={target}，core 返回 {rc}", targetUuid, rc);
                            Complete(targetUuid, false, "init_failed");
                            NativeCore.ClearPairingCode();
                        }
                        else
                        {
                            logger.LogInformation("PAIRING_INIT 流程结束: target={target}", targetUuid);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "发起配对异常: target={target}", targetUuid);
                        Complete(targetUuid, false, "init_failed");
                        NativeCore.ClearPairingCode();
                    }
                },
                CancellationToken.None);

            return (code, waiter.Task);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>core 的 RESULT 回调入口：完成对应等待器。</summary>
    public void Complete(string targetUuid, bool success, string reason)
    {
        TaskCompletionSource<bool>? waiter;
        lock (waiters)
        {
            if (success)
            {
                failureReasons.Remove(targetUuid);
            }
            else if (!string.IsNullOrEmpty(reason))
            {
                failureReasons[targetUuid] = reason;
            }
            if (!waiters.Remove(targetUuid, out waiter))
            {
                return;
            }
        }
        waiter.TrySetResult(success);
    }

    /// <summary>该目标是否存在进行中的发起流程。</summary>
    public bool HasPending(string targetUuid)
    {
        lock (waiters)
        {
            return waiters.ContainsKey(targetUuid);
        }
    }

    /// <summary>读取该目标最近一次失败原因（不消费）。</summary>
    public string? FailureReason(string targetUuid)
    {
        lock (waiters)
        {
            return failureReasons.TryGetValue(targetUuid, out var r) ? r : null;
        }
    }

    /// <summary>取消等待并清理 core 侧配对码。</summary>
    public void Cancel(string targetUuid)
    {
        TaskCompletionSource<bool>? waiter;
        lock (waiters)
        {
            waiters.Remove(targetUuid, out waiter);
        }
        waiter?.TrySetCanceled();
        NativeCore.ClearPairingCode();
    }

    /// <summary>把 core 的失败原因码转换为用户可读提示。</summary>
    public static string Describe(string? reason, string fallback) =>
        reason switch
        {
            "version_mismatch" => "core 版本不兼容：两端需升级到同一版本（major.minor 一致）",
            "no_target_ip" => "无法解析对端 IP：请等待该设备出现在可配对列表中",
            "connection_timeout" => "连接对端超时",
            "pairing_resp_timeout" => "等待对端响应超时",
            "session_missing" => "配对会话已失效，请重试",
            _ => fallback,
        };
}
