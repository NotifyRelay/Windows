using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

// 原生 FFI 绑定入口：常量、字符串辅助方法以及各功能域的 partial 拆分文件（NotifyRelayCore.*.cs）。
public static partial class NotifyRelayCore
{
    private const string DllName = "notify_relay_core";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_free_string(IntPtr s);

    public static string? PtrToStringAndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        var result = Marshal.PtrToStringUTF8(ptr);
        nrc_free_string(ptr);
        return result;
    }

    private static IntPtr StringToPtr(string? s)
    {
        if (s == null) return IntPtr.Zero;
        var bytes = Encoding.UTF8.GetBytes(s);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return ptr;
    }
}
