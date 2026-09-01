using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static partial class NotifyRelayCore
{
    // ======== Clipboard ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_clipboard_on_changed(IntPtr ctx, long queuePtr, IntPtr targetsJson, IntPtr mime, IntPtr content, long nowMs, int force);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_clipboard_on_received(IntPtr ctx, IntPtr payloadJson, long nowMs);

    public static partial class Safe
    {
        public static string? ClipboardOnChanged(IntPtr ctx, long queuePtr, string targetsJson, string mime, string content, long nowMs, bool force)
        {
            var t = StringToPtr(targetsJson); var m = StringToPtr(mime); var c = StringToPtr(content);
            var result = NotifyRelayCore.nrc_clipboard_on_changed(ctx, queuePtr, t, m, c, nowMs, force ? 1 : 0);
            Marshal.FreeHGlobal(t); Marshal.FreeHGlobal(m); Marshal.FreeHGlobal(c);
            return PtrToStringAndFree(result);
        }

        public static string? ClipboardOnReceived(IntPtr ctx, string payloadJson, long nowMs)
        {
            var p = StringToPtr(payloadJson);
            var result = NotifyRelayCore.nrc_clipboard_on_received(ctx, p, nowMs);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }
    }
}
