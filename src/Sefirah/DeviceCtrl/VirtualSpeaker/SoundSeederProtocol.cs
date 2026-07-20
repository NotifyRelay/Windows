using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NotifyRelay.DeviceCtrl.VirtualSpeaker;

public static class SoundSeederProtocol
{
    public const int MulticastSendPort = 33323;
    public const int MulticastListenPort = 33324;
    public const int ControlPort = 42440;
    public const int AudioPort = 5353;
    public const int PlayerVersion = 130;
    public const string MulticastAddress = "233.3.33.23";

    public static int JavaStringHashCode(string str)
    {
        int hash = 0;
        foreach (char c in str)
        {
            hash = 31 * hash + c;
        }
        return hash;
    }

    public static string GenerateUuid(string? ip = null)
    {
        if (ip == null)
        {
            ip = GetLocalIpAddress();
        }
        return "SE" + JavaStringHashCode(ip ?? "unknown");
    }

    public static string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList.FirstOrDefault(
                ip => ip.AddressFamily == AddressFamily.InterNetwork
                      && !IPAddress.IsLoopback(ip))?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public static int ChannelsToFormatCode(int channels) => channels switch
    {
        1 => 4,
        2 => 12,
        4 => 204,
        6 => 252,
        8 => 1020,
        _ => 12,
    };

    public static int BitsToChannelCode(int bitsPerSample) => bitsPerSample switch
    {
        8 => 3,
        16 => 2,
        _ => 2,
    };

    public static byte[] BuildAudioPacket(
        bool isReset,
        int sampleRate,
        int channels,
        int bitsPerSample,
        long timestamp,
        byte[] pcmData)
    {
        var formatCode = ChannelsToFormatCode(channels);
        var channelCode = BitsToChannelCode(bitsPerSample);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(isReset);
        writer.Write(sampleRate);
        writer.Write(formatCode);
        writer.Write(channelCode);
        writer.Write(timestamp);
        writer.Write(0L); // offset
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        return ms.ToArray();
    }

    public static byte[] Float32ToPcm16(byte[] floatBuffer, int bytesRecorded)
    {
        var sampleCount = bytesRecorded / 4;
        var pcm16 = new byte[sampleCount * 2];

        for (int i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToSingle(floatBuffer, i * 4);
            var clamped = Math.Clamp(sample, -1.0f, 1.0f);
            var shortVal = (short)(clamped * 32767f);
            pcm16[i * 2] = (byte)(shortVal & 0xFF);
            pcm16[i * 2 + 1] = (byte)((shortVal >> 8) & 0xFF);
        }

        return pcm16;
    }
}
