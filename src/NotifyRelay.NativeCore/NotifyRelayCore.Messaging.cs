using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static partial class NotifyRelayCore
{
    // ======== Sender queue ========
    // header 规范化、去重键推导与发送去重均由该入口内部完成
    // 返回：1=已入队 0=命中去重丢弃 -1=参数无效或队列未初始化
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_enqueue_message(IntPtr ctx, long queuePtr, IntPtr deviceUuid, IntPtr header, IntPtr plaintext);

    // ======== State merge (push full; receive via on_data) ========
    // isQuery: 1=查询回调响应推送（心跳查询发现变更后由平台推送），0=正常主动推送
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_push_superisland_state(IntPtr ctx, long queuePtr, IntPtr deviceUuid, IntPtr fullJson, int isEnd, int isQuery);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_push_media_state(IntPtr ctx, long queuePtr, IntPtr deviceUuid, IntPtr fullJson, int isEnd, int isQuery);

    public static partial class Safe
    {
        // ======== Sender queue ========
        public static int EnqueueMessage(IntPtr ctx, long queuePtr, string deviceUuid, string header, string plaintext)
        {
            var u = StringToPtr(deviceUuid);
            var h = StringToPtr(header); var p = StringToPtr(plaintext);
            var result = NotifyRelayCore.nrc_enqueue_message(ctx, queuePtr, u, h, p);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(h); Marshal.FreeHGlobal(p);
            return result;
        }

        // 推送「全量」超级岛/媒体状态给某设备；Rust 内部做 diff，合并后的全量经 on_data 回调回传。
        // queuePtr 为 SenderQueue 句柄（与 EnqueueMessage 共用同一队列）。
        // isQuery: true=查询回调响应推送（心跳查询发现变更后由平台推送），false=正常主动推送。
        public static int PushMediaState(IntPtr ctx, long queuePtr, string deviceUuid, string fullJson, bool isEnd, bool isQuery = false)
        {
            var u = StringToPtr(deviceUuid); var p = StringToPtr(fullJson);
            var result = NotifyRelayCore.nrc_push_media_state(ctx, queuePtr, u, p, isEnd ? 1 : 0, isQuery ? 1 : 0);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(p);
            return result;
        }
    }
}
