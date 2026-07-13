using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static class NativeCore
{
    private static IntPtr _ctx = IntPtr.Zero;
    private static bool _initialized = false;

    public static IntPtr Context => _ctx;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        var asmLocation = typeof(NotifyRelayCore).Assembly.Location;
        var checkDirs = new[] {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\')),
            Path.GetDirectoryName(asmLocation)
        };

        foreach (var dir in checkDirs)
        {
            if (dir == null) continue;
            var dllPath = Path.Combine(dir, "notify_relay_core.dll");
            if (File.Exists(dllPath))
            {
                NativeLibrary.Load(dllPath);
                break;
            }
        }

        _ctx = NotifyRelayCore.nrc_init();
    }

    public static void Destroy()
    {
        if (_ctx != IntPtr.Zero)
        {
            NotifyRelayCore.nrc_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }
    }

    public static int MigrateSharedSecret(string deviceUuid, byte[] aesKey)
    {
        return NotifyRelayCore.Safe.MigrateSharedSecret(_ctx, deviceUuid, aesKey);
    }

    public static int RemoveDevice(string deviceUuid)
    {
        return NotifyRelayCore.Safe.RemoveDevice(_ctx, deviceUuid);
    }

    public static string? EncryptMessage(string header, string localUuid, string localPubKey, string remoteUuid, string plaintext)
    {
        return NotifyRelayCore.Safe.EncryptMessage(_ctx, header, localUuid, localPubKey, remoteUuid, plaintext);
    }

    public static string? DecryptMessage(string encryptedLine)
    {
        return NotifyRelayCore.Safe.DecryptMessage(_ctx, encryptedLine);
    }

    public static string? FormatHeartbeat(string uuid, string nameB64, ushort port, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatHeartbeat(uuid, nameB64, port, battery, deviceType);
    }

    public static string? FormatDiscovery(string uuid, string nameB64, ushort port, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatDiscovery(uuid, nameB64, port, battery, deviceType);
    }

    public static string? FormatTcpHeartbeat(string uuid, string nameB64, ushort port, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatTcpHeartbeat(uuid, nameB64, port, battery, deviceType);
    }

    public static string? ParseHeartbeatJson(string line)
    {
        return NotifyRelayCore.Safe.ParseHeartbeatJson(line);
    }

    public static string? ParseHeartbeatTcpJson(string line)
    {
        return NotifyRelayCore.Safe.ParseHeartbeatTcpJson(line);
    }

    public static string? FormatPairingInit(string uuid, string tmpPubKey, string ip, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatPairingInit(uuid, tmpPubKey, ip, battery, deviceType);
    }

    public static string? FormatPairingResp(string uuid, string tmpPub, string ltPub, string encryptedCode, string ip, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatPairingResp(uuid, tmpPub, ltPub, encryptedCode, ip, battery, deviceType);
    }

    public static string? FormatAccept(string uuid, string ltPubKey, string ip, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatAccept(uuid, ltPubKey, ip, battery, deviceType);
    }

    public static string? FormatHandshake(string uuid, string pubKey, string ip, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatHandshake(uuid, pubKey, ip, battery, deviceType);
    }
}
