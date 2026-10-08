using System.Runtime.InteropServices;

namespace NotifyRelay.Platforms.Windows.Interop;

[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    void NotImplemented1();
    void NotImplemented2();
    void NotImplemented3();
    void NotImplemented4();
    void NotImplemented5();
    void NotImplemented6();
    void NotImplemented7();
    void NotImplemented8();
    void NotImplemented9();
    void NotImplemented10();

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.I4)] ERole role);
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

public static class InteropHelpers
{
    [DllImport("user32.dll")]
    public static extern void SetForegroundWindow(nint hWnd);

    // 以下三个 API 供单实例激活重定向使用：重定向必须等待完成，
    // 而等待期间必须继续泵消息，否则 RedirectActivationToAsync 无法推进
    // （见 WindowsAppSDK issue #1709）。
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateEvent(nint lpEventAttributes, bool bManualReset,
            bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    public static extern bool SetEvent(nint hEvent);

    [DllImport("ole32.dll")]
    public static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, nint[] pHandles, out uint dwIndex);
}
