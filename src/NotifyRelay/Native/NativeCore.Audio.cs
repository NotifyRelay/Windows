namespace NotifyRelay.Native;

public static partial class NativeCore
{
    public static int AudioStart(string direction, int sampleRate, int channels, string remoteUuid)
    {
        return NotifyRelayCore.Safe.AudioStart(_ctx, direction, 23335, sampleRate, channels, remoteUuid);
    }

    public static int AudioWriteFrame(byte[] pcm)
    {
        return NotifyRelayCore.Safe.AudioWriteFrame(_ctx, pcm, pcm.Length);
    }

    public static int AudioStop()
    {
        return NotifyRelayCore.Safe.AudioStop(_ctx);
    }

    public static int AudioIsActive()
    {
        return NotifyRelayCore.Safe.AudioIsActive(_ctx);
    }

    public static void RegisterAudioCallbacks(NotifyRelayCore.AudioDataCallback dataCb, NotifyRelayCore.AudioEventCallback eventCb)
    {
        NotifyRelayCore.Safe.RegisterAudioDataCb(_ctx, dataCb);
        NotifyRelayCore.Safe.RegisterAudioEventCb(_ctx, eventCb);
        _callbackRefs.Add(dataCb);
        _callbackRefs.Add(eventCb);
    }

    private static void HandleDeviceTimeout(string uuid)
    {
        var device = DeviceManager?.FindDeviceById(uuid);
        if (device == null) return;
        // 在线状态由 core 快照（online）判定，这里只清理平台侧的 TCP 会话引用与 core 会话
        device.Session = null;
        NetworkService?.DisconnectDevice(uuid);
    }
}
