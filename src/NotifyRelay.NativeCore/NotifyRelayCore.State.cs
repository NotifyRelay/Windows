using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static partial class NotifyRelayCore
{
    // ======== State persistence ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_export_state(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_import_state(IntPtr ctx, IntPtr json);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_encrypt_local_state(IntPtr ctx, IntPtr plaintext, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_decrypt_local_state(IntPtr ctx, IntPtr encryptedB64, IntPtr deviceUuid);

    public static partial class Safe
    {
        public static string? ExportState(IntPtr ctx)
        {
            return PtrToStringAndFree(nrc_export_state(ctx));
        }

        public static int ImportState(IntPtr ctx, string json)
        {
            var jsonPtr = StringToPtr(json);
            var result = nrc_import_state(ctx, jsonPtr);
            Marshal.FreeHGlobal(jsonPtr);
            return result;
        }

        public static string? EncryptLocalState(IntPtr ctx, string plaintext, string deviceUuid)
        {
            var p = StringToPtr(plaintext); var u = StringToPtr(deviceUuid);
            var result = nrc_encrypt_local_state(ctx, p, u);
            Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        public static string? DecryptLocalState(IntPtr ctx, string encryptedB64, string deviceUuid)
        {
            var e = StringToPtr(encryptedB64); var u = StringToPtr(deviceUuid);
            var result = nrc_decrypt_local_state(ctx, e, u);
            Marshal.FreeHGlobal(e); Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }
    }
}
