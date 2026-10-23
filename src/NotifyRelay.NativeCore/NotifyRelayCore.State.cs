using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_serialize_local_state(IntPtr ctx, IntPtr deviceUuid, int encrypt);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_deserialize_local_state(IntPtr ctx, IntPtr payload, IntPtr deviceUuid, int encrypted);

    public static partial class Safe
    {
        public static int ImportState(IntPtr ctx, string json)
        {
            var jsonPtr = StringToPtr(json);
            var result = nrc_import_state(ctx, jsonPtr);
            Marshal.FreeHGlobal(jsonPtr);
            return result;
        }

        public static string? DecryptLocalState(IntPtr ctx, string encryptedB64, string deviceUuid)
        {
            var e = StringToPtr(encryptedB64); var u = StringToPtr(deviceUuid);
            var result = nrc_decrypt_local_state(ctx, e, u);
            Marshal.FreeHGlobal(e); Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        /// <summary>本地状态序列化聚合入口：导出密钥状态，encrypt 非 0 时返回密文</summary>
        public static string? SerializeLocalState(IntPtr ctx, string deviceUuid, int encrypt)
        {
            var u = StringToPtr(deviceUuid);
            var result = nrc_serialize_local_state(ctx, u, encrypt);
            Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        /// <summary>本地状态反序列化聚合入口：encrypted 非 0 时先解密再导入</summary>
        /// <returns>true 表示 core 返回 {"ok":true}，即解密与导入均成功</returns>
        public static bool DeserializeLocalState(IntPtr ctx, string payload, string deviceUuid, int encrypted)
        {
            var p = StringToPtr(payload); var u = StringToPtr(deviceUuid);
            var result = nrc_deserialize_local_state(ctx, p, u, encrypted);
            Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(u);
            var json = PtrToStringAndFree(result);
            if (json == null) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
