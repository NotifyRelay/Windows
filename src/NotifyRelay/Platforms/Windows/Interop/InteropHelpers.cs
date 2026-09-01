using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;

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

public enum GetWindowLongFlags
{
    GWL_STYLE = -16,
    GWL_EXSTYLE = -20,
    GWL_HWNDPARENT = -8
}

public enum WindowStyles
{
    WS_CHILD = 0x40000000,
    WS_VISIBLE = 0x10000000,
    WS_DISABLED = 0x08000000
}

public enum ExtendedWindowStyles
{
    WS_EX_LAYERED = 0x00080000,
    WS_EX_TRANSPARENT = 0x00000020,
    WS_EX_TOOLWINDOW = 0x00000080,
    WS_EX_NOACTIVATE = 0x08000000
}


public static class InteropHelpers
{
    public static readonly Guid DataTransferManagerInteropIID = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateEvent(nint lpEventAttributes, bool bManualReset,
            bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    public static extern bool SetEvent(nint hEvent);

    [DllImport("ole32.dll")]
    public static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, nint[] pHandles, out uint dwIndex);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
            => (X, Y) = (x, y);
    }

    public static void ChangeCursor(this UIElement uiElement, InputCursor cursor)
    {
        Type type = typeof(UIElement);
        type.InvokeMember("ProtectedCursor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.SetProperty | BindingFlags.Instance, null, uiElement, new object[] { cursor });
    }

    [DllImport("user32.dll")]
    public static extern void SetForegroundWindow(nint hWnd);

    // 32 位 user32.dll 不导出 *WindowLongPtrW（该名称在 Win32 头文件中仅为 64 位的宏别名），
    // 因此按 nint.Size 分派：64 位走 *WindowLongPtrW，32 位走 *WindowLongW。
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLongW(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);

    /// <summary>读取窗口 long 值（32/64 位兼容包装）。</summary>
    public static nint GetWindowLongPtr(nint hWnd, GetWindowLongFlags nIndex)
        => nint.Size == 8
            ? GetWindowLongPtrW(hWnd, (int)nIndex)
            : new nint(GetWindowLongW(hWnd, (int)nIndex));

    /// <summary>写入窗口 long 值（32/64 位兼容包装）。</summary>
    public static nint SetWindowLongPtr(nint hWnd, GetWindowLongFlags nIndex, nint dwNewLong)
        => nint.Size == 8
            ? SetWindowLongPtrW(hWnd, (int)nIndex, dwNewLong)
            : new nint(SetWindowLongW(hWnd, (int)nIndex, dwNewLong.ToInt32()));

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint FindWindowEx(nint hWndParent, nint hWndChildAfter, [MarshalAs(UnmanagedType.LPWStr)] string lpszClass, [MarshalAs(UnmanagedType.LPWStr)] string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetDesktopWindow();

    public const uint LWA_COLORKEY = 0x00000001;
    public const uint LWA_ALPHA = 0x00000002;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public const nint HWND_BOTTOM = 1;
    public const nint HWND_TOP = 0;
    public const nint HWND_TOPMOST = -1;
    public const nint HWND_NOTOPMOST = -2;

    public static nint GetWallpaperWindow()
    {
        nint progman = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
        nint workerw = IntPtr.Zero;

        if (progman != IntPtr.Zero)
        {
            workerw = FindWindowEx(IntPtr.Zero, progman, "WorkerW", null);
            while (workerw != IntPtr.Zero)
            {
                nint ssheldwnd = FindWindowEx(workerw, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (ssheldwnd != IntPtr.Zero)
                {
                    workerw = FindWindowEx(IntPtr.Zero, workerw, "WorkerW", null);
                    continue;
                }
                break;
            }
        }

        return workerw != IntPtr.Zero ? workerw : progman;
    }

    public static void SetWindowToWallpaperLayer(nint hWnd)
    {
        nint progman = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
        nint wallpaperWorkerw = IntPtr.Zero;
        nint iconsWorkerw = IntPtr.Zero;

        if (progman != IntPtr.Zero)
        {
            SendMessageTimeout(progman, 0x052C, 0, 0, 0, 1000, out _);

            nint currentWorkerw = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null);
            while (currentWorkerw != IntPtr.Zero)
            {
                nint ssheldwnd = FindWindowEx(currentWorkerw, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (ssheldwnd != IntPtr.Zero)
                {
                    iconsWorkerw = currentWorkerw;
                }
                else
                {
                    wallpaperWorkerw = currentWorkerw;
                }
                currentWorkerw = FindWindowEx(IntPtr.Zero, currentWorkerw, "WorkerW", null);
            }
        }

        nint targetParent = wallpaperWorkerw != IntPtr.Zero ? wallpaperWorkerw : progman;

        if (targetParent == IntPtr.Zero)
        {
            return;
        }

        nint exStyle = GetWindowLongPtr(hWnd, GetWindowLongFlags.GWL_EXSTYLE);
        exStyle |= (nint)(ExtendedWindowStyles.WS_EX_LAYERED | ExtendedWindowStyles.WS_EX_NOACTIVATE);
        exStyle &= ~(nint)(ExtendedWindowStyles.WS_EX_TOOLWINDOW | ExtendedWindowStyles.WS_EX_TRANSPARENT);
        SetWindowLongPtr(hWnd, GetWindowLongFlags.GWL_EXSTYLE, exStyle);

        nint style = GetWindowLongPtr(hWnd, GetWindowLongFlags.GWL_STYLE);
        style |= (nint)(WindowStyles.WS_CHILD | WindowStyles.WS_VISIBLE);
        style &= ~(nint)WindowStyles.WS_DISABLED;
        SetWindowLongPtr(hWnd, GetWindowLongFlags.GWL_STYLE, style);

        SetParent(hWnd, targetParent);

        SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);

        if (iconsWorkerw != IntPtr.Zero)
        {
            SetWindowPos(hWnd, iconsWorkerw, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
        else
        {
            SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern nint SendMessageTimeout(nint hWnd, uint Msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);
}
