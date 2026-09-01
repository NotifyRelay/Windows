using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static partial class NotifyRelayCore
{
    // ======== Audio stream ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_audio_start(IntPtr ctx, IntPtr direction, int port, int sampleRate, int channels, IntPtr remoteUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_audio_write_frame(IntPtr ctx, [In] byte[] pcmData, int pcmLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_audio_stop(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_audio_is_active(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_register_audio_data_cb(IntPtr ctx, AudioDataCallback cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_register_audio_event_cb(IntPtr ctx, AudioEventCallback cb);

    // ======== Audio callback delegate types ========
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioDataCallback(IntPtr deviceUuid, IntPtr pcmData, int pcmLen, int sampleRate, int channels, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioEventCallback(IntPtr deviceUuid, IntPtr eventStr, IntPtr errorMsg, IntPtr userData);

    public static partial class Safe
    {
        public static int AudioStart(IntPtr ctx, string direction, int port, int sampleRate, int channels, string remoteUuid)
        {
            var d = StringToPtr(direction);
            var u = StringToPtr(remoteUuid);
            var result = NotifyRelayCore.nrc_audio_start(ctx, d, port, sampleRate, channels, u);
            Marshal.FreeHGlobal(d);
            Marshal.FreeHGlobal(u);
            return result;
        }

        public static int AudioWriteFrame(IntPtr ctx, byte[] pcmData, int pcmLen)
        {
            return NotifyRelayCore.nrc_audio_write_frame(ctx, pcmData, pcmLen);
        }

        public static int AudioStop(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_audio_stop(ctx);
        }

        public static int AudioIsActive(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_audio_is_active(ctx);
        }

        public static void RegisterAudioDataCb(IntPtr ctx, AudioDataCallback cb)
        {
            NotifyRelayCore.nrc_register_audio_data_cb(ctx, cb);
        }

        public static void RegisterAudioEventCb(IntPtr ctx, AudioEventCallback cb)
        {
            NotifyRelayCore.nrc_register_audio_event_cb(ctx, cb);
        }
    }
}
