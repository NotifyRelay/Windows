using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static partial class NotifyRelayCore
{
    // ======== App sync (app list & icons) ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_app_sync_prepare_icon_request(IntPtr ctx, IntPtr packagesJson, IntPtr installedJson, IntPtr cachedJson, IntPtr appDeviceJson, IntPtr sourceDeviceUuid, long nowMs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_app_sync_clear_icon_pending(IntPtr ctx, IntPtr packagesJson);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_app_sync_parse_icon_response(IntPtr payloadJson);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_app_sync_build_applist_request(IntPtr scope, long nowMs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_app_sync_parse_applist_response(IntPtr payloadJson);

    public static partial class Safe
    {
        public static string? AppSyncPrepareIconRequest(IntPtr ctx, string packagesJson, string installedJson, string cachedJson, string appDeviceJson, string sourceDeviceUuid, long nowMs)
        {
            var pk = StringToPtr(packagesJson); var in_ = StringToPtr(installedJson); var ca = StringToPtr(cachedJson);
            var ad = StringToPtr(appDeviceJson); var su = StringToPtr(sourceDeviceUuid);
            var result = NotifyRelayCore.nrc_app_sync_prepare_icon_request(ctx, pk, in_, ca, ad, su, nowMs);
            Marshal.FreeHGlobal(pk); Marshal.FreeHGlobal(in_); Marshal.FreeHGlobal(ca); Marshal.FreeHGlobal(ad); Marshal.FreeHGlobal(su);
            return PtrToStringAndFree(result);
        }

        public static void AppSyncClearIconPending(IntPtr ctx, string packagesJson)
        {
            var pk = StringToPtr(packagesJson);
            NotifyRelayCore.nrc_app_sync_clear_icon_pending(ctx, pk);
            Marshal.FreeHGlobal(pk);
        }

        public static string? AppSyncParseIconResponse(string payloadJson)
        {
            var p = StringToPtr(payloadJson);
            var result = NotifyRelayCore.nrc_app_sync_parse_icon_response(p);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }

        public static string? AppSyncBuildApplistRequest(string scope, long nowMs)
        {
            var s = StringToPtr(scope);
            var result = NotifyRelayCore.nrc_app_sync_build_applist_request(s, nowMs);
            Marshal.FreeHGlobal(s);
            return PtrToStringAndFree(result);
        }

        public static string? AppSyncParseApplistResponse(string payloadJson)
        {
            var p = StringToPtr(payloadJson);
            var result = NotifyRelayCore.nrc_app_sync_parse_applist_response(p);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }
    }
}
