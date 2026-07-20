using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NotifyRelay.DeviceCtrl.VirtualSpeaker;

public static class SoundSeederProtocol
{
    public const int PlayerListenPort = 33323;
    public const int SpeakerListenPort = 33324;
    public const int ControlPort = 42440;
    public const int AudioPort = 42441;
    public const int HeartbeatPort = 5353;
    public const int PlayerVersion = 191;
    public const string MulticastAddress = "233.3.33.23";

    public static int JavaStringHashCode(string str)
    {
        int hash = 0;
        foreach (char c in str)
            hash = 31 * hash + c;
        return hash;
    }

    public static string GenerateUuid(string? ip = null)
    {
        if (ip == null)
            ip = GetLocalIpAddress();
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

    private static void WriteInt32BE(BinaryWriter writer, int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static void WriteInt64BE(BinaryWriter writer, long value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        writer.Write(bytes);
    }

    public static byte[] BuildAudioPacket(
        bool isReset,
        int sampleRate,
        int channels,
        int bitsPerSample,
        long timestamp,
        byte[] pcmData,
        long cumulativeOffsetMs = 0)
    {
        var formatCode = ChannelsToFormatCode(channels);
        var channelCode = BitsToChannelCode(bitsPerSample);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(isReset);
        WriteInt32BE(writer, sampleRate);
        WriteInt32BE(writer, formatCode);
        WriteInt32BE(writer, channelCode);
        WriteInt64BE(writer, timestamp);
        WriteInt64BE(writer, cumulativeOffsetMs);
        WriteInt32BE(writer, pcmData.Length);
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

    public static async Task<string?> SendShortCommandAsync(
        string ip, int port, string command, string? param = null, bool readResponse = false, int timeoutMs = 5000)
    {
        using var client = new TcpClient();
        using var connectCts = new CancellationTokenSource(timeoutMs);
        await client.ConnectAsync(ip, port, connectCts.Token);
        client.ReceiveTimeout = timeoutMs;
        using var stream = client.GetStream();

        var cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
        await stream.WriteAsync(cmdBytes, 0, cmdBytes.Length);
        await stream.FlushAsync();

        if (param != null)
        {
            var paramBytes = Encoding.UTF8.GetBytes(param + "\n");
            await stream.WriteAsync(paramBytes, 0, paramBytes.Length);
            await stream.FlushAsync();
        }

        if (readResponse)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
            using var responseCts = new CancellationTokenSource(timeoutMs);
            return await reader.ReadLineAsync(responseCts.Token);
        }

        return null;
    }

    public static async Task SendShortCommandNoResponseAsync(
        string ip, int port, string command, string? param = null, int timeoutMs = 5000)
    {
        await SendShortCommandAsync(ip, port, command, param, false, timeoutMs);
    }

    public static async Task<string?> SendShortCommandWithResponseAsync(
        string ip, int port, string command, string? param = null, int timeoutMs = 5000)
    {
        return await SendShortCommandAsync(ip, port, command, param, true, timeoutMs);
    }

    public static async Task<List<string>> SendMultiCommandWithResponsesAsync(
        string ip, int port, IEnumerable<(string Command, string? Param)> commands, int timeoutMs = 5000)
    {
        using var client = new TcpClient();
        using var connectCts = new CancellationTokenSource(timeoutMs);
        await client.ConnectAsync(ip, port, connectCts.Token);
        client.ReceiveTimeout = timeoutMs;
        using var stream = client.GetStream();

        var responses = new List<string>();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);

        foreach (var (cmd, param) in commands)
        {
            var cmdBytes = Encoding.UTF8.GetBytes(cmd + "\n");
            await stream.WriteAsync(cmdBytes, 0, cmdBytes.Length);
            await stream.FlushAsync();

            if (param != null)
            {
                var paramBytes = Encoding.UTF8.GetBytes(param + "\n");
                await stream.WriteAsync(paramBytes, 0, paramBytes.Length);
                await stream.FlushAsync();
            }

            // 读取响应（所有命令都有响应，即使是空行）
            using var responseCts = new CancellationTokenSource(timeoutMs);
            var response = await reader.ReadLineAsync(responseCts.Token);
            responses.Add(response ?? "");
        }

        return responses;
    }
}
