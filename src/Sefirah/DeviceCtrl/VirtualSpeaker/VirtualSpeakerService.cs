using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NotifyRelay.Data.Contracts;

namespace NotifyRelay.DeviceCtrl.VirtualSpeaker;

public enum StreamingStrategy
{
    /// <summary>原始逐包：每个 WASAPI 回调直接发包，首包 isReset=true，timestamp 递增</summary>
    PerCallback,
    /// <summary>积累 200ms + 全部 timestamp=0（跳过 Speaker 时序计算）</summary>
    AccumZero200,
    /// <summary>积累 500ms + 全部 timestamp=0</summary>
    AccumZero500,
    /// <summary>积累 1000ms + 全部 timestamp=0</summary>
    AccumZero1000,
    /// <summary>积累 200ms + 正常递增 timestamp</summary>
    AccumTrue200,
    /// <summary>积累 200ms + 正常递增 timestamp + 20ms buffer</summary>
    AccumBuf200,
    /// <summary>模仿官方客户端：~23ms逐包 + 高精度时间戳（44100Hz下约23ms/包）</summary>
    Official,
}

public class VirtualSpeakerService : IDisposable
{
    private readonly ILogger<VirtualSpeakerService> _logger;
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly MMDeviceEnumerator _enumerator = new();

    private UdpClient? _discoveryClient;
    private CancellationTokenSource? _discoveryCts;

    private TcpClient? _controlClient;
    private NetworkStream? _controlStream;

    private TcpListener? _audioListener;
    private TcpClient? _audioClient;
    private NetworkStream? _audioStream;
    private TcpListener? _heartbeatListener;
    private CancellationTokenSource? _heartbeatCts;
    private string? _targetSpeakerIp;

    private WasapiLoopbackCapture? _capture;
    private CancellationTokenSource? _streamingCts;
    private Thread? _audioWriterThread;
    private readonly AutoResetEvent _audioDataReady = new(false);
    private readonly ConcurrentQueue<(byte[] Buffer, DateTime CapturedAt)> _audioQueue = new();
    private const int MaxAudioAgeMs = 2000; // 超过2秒的音频直接丢弃
    private const long PerCallbackBufMs = 25; // 缓冲(ms)，需 > Speaker this.d + 网络RTT/2

    private bool _isRunning;
    private bool _isDisposed;
    private bool _systemWasMuted;
    private string? _playerUuid;
    private int _sampleRate;
    private int _channels;
    private bool _isFirstPacket = true;
    private long _timestampBase;
    private long _clockOffsetMs; // Speaker nanoTime 偏移量

    private static long NowMs => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

    // 可通过 UI 下拉框切换的流式策略（默认 AccumZero200）
    private StreamingStrategy _strategy = StreamingStrategy.AccumZero200;
    public StreamingStrategy Strategy { get => _strategy; set => _strategy = value; }

    private readonly List<SoundSeederDeviceInfo> _discoveredSpeakers = [];
    private readonly object _speakersLock = new();

    public event EventHandler? StatusChanged;
    public event EventHandler<SoundSeederDeviceInfo>? SpeakerDiscovered;

    public bool IsRunning => _isRunning;

    public VirtualSpeakerService(
        ILogger<VirtualSpeakerService> logger,
        IGeneralSettingsService generalSettingsService)
    {
        _logger = logger;
        _generalSettingsService = generalSettingsService;
    }

    public async Task<List<SoundSeederDeviceInfo>> DiscoverSpeakersAsync(int timeoutMs = 5000)
    {
        lock (_speakersLock) _discoveredSpeakers.Clear();

        _playerUuid = SoundSeederProtocol.GenerateUuid();
        _discoveryCts = new CancellationTokenSource();
        var token = _discoveryCts.Token;

        try
        {
            _discoveryClient = new UdpClient(SoundSeederProtocol.MulticastSendPort);
            _discoveryClient.JoinMulticastGroup(IPAddress.Parse(SoundSeederProtocol.MulticastAddress));
            _logger.LogInformation("已加入多播组 {Address}:{Port}",
                SoundSeederProtocol.MulticastAddress, SoundSeederProtocol.MulticastSendPort);

            var probeBytes = Encoding.UTF8.GetBytes(_playerUuid);
            var listenTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _discoveryClient.ReceiveAsync(token);
                        var uuid = Encoding.UTF8.GetString(result.Buffer).TrimEnd('\0');
                        var ip = result.RemoteEndPoint.Address.ToString();

                        if (uuid == _playerUuid) continue;

                        lock (_speakersLock)
                        {
                            if (_discoveredSpeakers.Any(s => s.Uuid == uuid))
                                continue;

                            var info = new SoundSeederDeviceInfo
                            {
                                Uuid = uuid,
                                Name = uuid,
                                IpAddress = ip,
                                Version = SoundSeederProtocol.PlayerVersion
                            };
                            _discoveredSpeakers.Add(info);
                            SpeakerDiscovered?.Invoke(this, info);
                            _logger.LogInformation("发现 SoundSeeder 扬声器: {Uuid} ({Ip})", uuid, ip);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "发现监听异常");
                    }
                }
            }, token);

            var probeTask = Task.Run(async () =>
            {
                var endTime = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (!token.IsCancellationRequested && DateTime.UtcNow < endTime)
                {
                    try
                    {
                        await _discoveryClient.SendAsync(probeBytes, probeBytes.Length,
                            SoundSeederProtocol.MulticastAddress, SoundSeederProtocol.MulticastListenPort);
                        await Task.Delay(800, token);
                    }
                    catch { break; }
                }
            }, token);

            await Task.WhenAny(Task.Delay(timeoutMs, token), probeTask);
            _discoveryCts.Cancel();

            try { await listenTask; } catch { }
            try { await probeTask; } catch { }

            lock (_speakersLock)
            {
                if (_discoveredSpeakers.Count == 0)
                {
                    TryProbeLocalSpeaker();
                }
                _logger.LogInformation("发现完成，找到 {Count} 个 SoundSeeder 设备", _discoveredSpeakers.Count);
                return [.. _discoveredSpeakers];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设备发现失败");
            return [];
        }
        finally
        {
            StopDiscovery();
        }
    }

    private void TryProbeLocalSpeaker()
    {
        var probeIps = new[] { "127.0.0.1", "192.168.31.137" };
        foreach (var ip in probeIps)
        {
            try
            {
                using var testClient = new TcpClient();
                var connectTask = testClient.ConnectAsync(ip, SoundSeederProtocol.ControlPort);
                if (connectTask.Wait(TimeSpan.FromSeconds(1)))
                {
                    var localUuid = "SE" + SoundSeederProtocol.JavaStringHashCode(ip);
                    var info = new SoundSeederDeviceInfo
                    {
                        Uuid = localUuid,
                        Name = $"本地 Speaker ({ip})",
                        IpAddress = ip,
                        Version = SoundSeederProtocol.PlayerVersion
                    };
                    _discoveredSpeakers.Add(info);
                    SpeakerDiscovered?.Invoke(this, info);
                    _logger.LogInformation("发现本地 Speaker: {Ip}", ip);
                }
            }
            catch { }
        }
    }

    private void StartHeartbeatListener()
    {
        try { _heartbeatListener?.Stop(); } catch { }
        _heartbeatListener = null;
        try { _heartbeatCts?.Cancel(); } catch { }
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;

        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;
        _heartbeatListener = new TcpListener(IPAddress.Any, SoundSeederProtocol.HeartbeatPort);
        _heartbeatListener.Start();
        _logger.LogInformation("心跳监听已启动 :{Port}", SoundSeederProtocol.HeartbeatPort);

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var client = await _heartbeatListener.AcceptTcpClientAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }, token);
    }

    private void StopDiscovery()
    {
        try { _discoveryClient?.DropMulticastGroup(IPAddress.Parse(SoundSeederProtocol.MulticastAddress)); } catch { }
        try { _discoveryClient?.Close(); } catch { }
        try { _discoveryClient?.Dispose(); } catch { }
        _discoveryClient = null;
    }

    private void ClearAudioQueue()
    {
        while (_audioQueue.TryDequeue(out _)) { }
    }

    public async Task StartStreaming()
    {
        if (_isRunning)
        {
            _logger.LogInformation("虚拟扬声器已在运行");
            return;
        }

        var deviceId = _generalSettingsService.VirtualSpeakerTargetDeviceId;
        var deviceName = _generalSettingsService.VirtualSpeakerTargetDeviceName;
        if (string.IsNullOrEmpty(deviceId))
        {
            _logger.LogError("未选择目标SoundSeeder设备，请在设置中扫描并选择设备");
            return;
        }

        SoundSeederDeviceInfo? targetSpeaker;
        lock (_speakersLock)
            targetSpeaker = _discoveredSpeakers.FirstOrDefault(s => s.Uuid == deviceId);

        if (targetSpeaker == null)
        {
            var savedIp = _generalSettingsService.VirtualSpeakerTargetDeviceIp;
            if (!string.IsNullOrEmpty(savedIp))
            {
                targetSpeaker = new SoundSeederDeviceInfo
                {
                    Uuid = deviceId,
                    Name = deviceName ?? deviceId,
                    IpAddress = savedIp,
                    Version = SoundSeederProtocol.PlayerVersion
                };
                _logger.LogInformation("使用保存的IP直连扬声器: {Ip}", savedIp);
            }
            else
            {
                _logger.LogError("目标 SoundSeeder 设备不在已发现列表中，且无保存的IP: {DeviceId}", deviceId);
                return;
            }
        }

        _playerUuid = SoundSeederProtocol.GenerateUuid();
        _isFirstPacket = true;
        ClearAudioQueue();

        try
        {
            if (_generalSettingsService.VirtualSpeakerMuteOnStart)
            {
                var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice is { AudioEndpointVolume.Mute: false })
                {
                    defaultDevice.AudioEndpointVolume.Mute = true;
                    _systemWasMuted = true;
                }
            }

            _streamingCts = new CancellationTokenSource();
            var token = _streamingCts.Token;

            _controlClient = new TcpClient();
            await _controlClient.ConnectAsync(targetSpeaker.IpAddress, SoundSeederProtocol.ControlPort);
            _controlStream = _controlClient.GetStream();
            _logger.LogInformation("控制通道已连接 {Ip}:{Port}",
                targetSpeaker.IpAddress, SoundSeederProtocol.ControlPort);

            _targetSpeakerIp = targetSpeaker.IpAddress;

            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _channels = _capture.WaveFormat.Channels;
            _logger.LogInformation("音频捕获格式: {Rate}Hz {Channels}ch", _sampleRate, _channels);

            StartHeartbeatListener();

            _audioListener = new TcpListener(IPAddress.Any, SoundSeederProtocol.AudioPort);
            _audioListener.Start();
            _logger.LogInformation("音频服务已启动，等待 Speaker 连接 :{Port}", SoundSeederProtocol.AudioPort);

            await SendControlAsync("$setPlayer$",
                $"[\"{_playerUuid}\",\"{Dns.GetHostName()}\",{SoundSeederProtocol.PlayerVersion}]");
            await SendControlAsync("$idP$", _playerUuid);
            await SendControlAsync("$setv$", "15");
            var channelConf = _channels >= 2 ? "1" : "0"; // 0=Mono, 1=Stereo, 2=Left, 3=Right
            await SendControlAsync("$setch$", channelConf);
            await SendControlAsync("$setOffM$", "0");

            // 时钟同步：获取 Speaker 的 nanoTime 以校正两端时钟差
            var speakerNanoTimeStr = await SendControlWithResponseAsync("$off$");
            SyncClocks(speakerNanoTimeStr);

            await SendControlAsync("$con$");
            _logger.LogInformation("已请求音频连接，等待 Speaker 连接...");

            try
            {
                using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                _audioClient = await _audioListener.AcceptTcpClientAsync(acceptCts.Token);
                _audioStream = _audioClient.GetStream();
                _isFirstPacket = true;
                _logger.LogInformation("Speaker 音频通道已连接");
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("等待 Speaker 音频连接超时");
                return;
            }
            finally
            {
                try { _audioListener?.Stop(); } catch { }
                _audioListener = null;
            }

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _audioWriterThread = new Thread(() => AudioWriterLoop(token))
            {
                IsBackground = true,
                Name = "SoundSeeder-AudioWriter"
            };
            _audioWriterThread.Start();

            _timestampBase = NowMs + _clockOffsetMs;
            _capture.StartRecording();

            _isRunning = true;
            _generalSettingsService.EnableVirtualSpeaker = true;
            _logger.LogInformation("虚拟扬声器已启动，目标: {Name}", deviceName ?? deviceId);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动虚拟扬声器失败");
            await StopStreamingAsyncCore();
        }
    }

    private async Task SendControlAsync(string command, string? param = null)
    {
        if (_controlStream == null) return;
        var cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
        await _controlStream.WriteAsync(cmdBytes, 0, cmdBytes.Length);
        await _controlStream.FlushAsync();

        if (param != null)
        {
            var paramBytes = Encoding.UTF8.GetBytes(param + "\n");
            await _controlStream.WriteAsync(paramBytes, 0, paramBytes.Length);
            await _controlStream.FlushAsync();
        }
    }

    private async Task<string?> SendControlWithResponseAsync(string command, string? param = null)
    {
        if (_controlStream == null) return null;
        var cmdBytes = Encoding.UTF8.GetBytes(command + "\n");
        await _controlStream.WriteAsync(cmdBytes, 0, cmdBytes.Length);
        if (param != null)
        {
            var paramBytes = Encoding.UTF8.GetBytes(param + "\n");
            await _controlStream.WriteAsync(paramBytes, 0, paramBytes.Length);
        }
        await _controlStream.FlushAsync();

        using var reader = new StreamReader(_controlStream, Encoding.UTF8, leaveOpen: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        return await reader.ReadLineAsync(cts.Token);
    }

    private void SyncClocks(string? speakerNanoTimeStr)
    {
        if (long.TryParse(speakerNanoTimeStr, out var speakerNanoTime))
        {
            // nanoTime → 毫秒
            var speakerMs = speakerNanoTime / 1_000_000;
            var localMs = NowMs;
            _clockOffsetMs = speakerMs - localMs;
            _logger.LogInformation(
                "时钟同步完成: Speaker={SpeakerMs}ms, Local={LocalMs}ms, Offset={Offset}ms",
                speakerMs, localMs, _clockOffsetMs);
        }
        else
        {
            _logger.LogWarning("时钟同步失败: 无法解析 Speaker 时间 '{Response}'，非零 timestamp 模式可能不工作",
                speakerNanoTimeStr ?? "(null)");
            _clockOffsetMs = 0;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        var buffer = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
        _audioQueue.Enqueue((buffer, DateTime.UtcNow));
        _audioDataReady.Set();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _logger.LogInformation("音频捕获已停止");
    }

    private void AudioWriterLoop(CancellationToken token)
    {
        if (_strategy == StreamingStrategy.PerCallback)
            AudioWriterPerCallback(token);
        else
            AudioWriterAccumulate(token);
    }

    private void AudioWriterPerCallback(CancellationToken token)
    {
        long streamTimestamp = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_audioDataReady.WaitOne(100))
                {
                    DrainPerCallback(ref streamTimestamp, token);
                    continue;
                }

                DrainPerCallback(ref streamTimestamp, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "音频写入异常");
            }
        }
    }

    private void DrainPerCallback(ref long streamTimestamp, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        while (_audioQueue.TryDequeue(out var entry))
        {
            if ((now - entry.CapturedAt).TotalMilliseconds > MaxAudioAgeMs)
                continue; // 丢弃过期数据

            var pcm16 = SoundSeederProtocol.Float32ToPcm16(entry.Buffer, entry.Buffer.Length);

            var packet = SoundSeederProtocol.BuildAudioPacket(
                _isFirstPacket, _sampleRate, _channels, 16,
                NowMs + _clockOffsetMs + PerCallbackBufMs, pcm16);
            _isFirstPacket = false;

            WritePacket(packet, token);
        }
    }

    private void AudioWriterAccumulate(CancellationToken token)
    {
        var accumulatedPcmStream = new MemoryStream();
        long accumulatedFrames = 0;
        long streamTimestamp = 0;

        int targetMs = _strategy switch
        {
            StreamingStrategy.Official => 23,
            StreamingStrategy.AccumZero500 => 500,
            StreamingStrategy.AccumZero1000 => 1000,
            _ => 200,
        };
        bool useTimestampZero = _strategy == StreamingStrategy.AccumZero200
                             || _strategy == StreamingStrategy.AccumZero500
                             || _strategy == StreamingStrategy.AccumZero1000;
        bool useTimestampBuf = _strategy == StreamingStrategy.AccumBuf200
                            || _strategy == StreamingStrategy.AccumTrue200
                            || _strategy == StreamingStrategy.Official;
        long bufMs = _strategy switch
        {
            StreamingStrategy.AccumBuf200 => 25,
            StreamingStrategy.AccumTrue200 => 25,
            StreamingStrategy.Official => 25,
            _ => 0
        };
        var targetFrames = _sampleRate * targetMs / 1000;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_audioDataReady.WaitOne(100))
                {
                    DrainAccumulate(accumulatedPcmStream, ref accumulatedFrames,
                        targetFrames, useTimestampZero, useTimestampBuf, bufMs,
                        ref streamTimestamp, token);
                    continue;
                }

                DrainAccumulate(accumulatedPcmStream, ref accumulatedFrames,
                    targetFrames, useTimestampZero, useTimestampBuf, bufMs,
                    ref streamTimestamp, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "音频写入异常");
            }
        }
    }

    private void DrainAccumulate(MemoryStream accStream, ref long accFrames,
        int targetFrames, bool useTimestampZero, bool useTimestampBuf, long bufMs,
        ref long streamTimestamp, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        while (_audioQueue.TryDequeue(out var entry))
        {
            if ((now - entry.CapturedAt).TotalMilliseconds > MaxAudioAgeMs)
                continue; // 丢弃过期数据

            var pcm16 = SoundSeederProtocol.Float32ToPcm16(entry.Buffer, entry.Buffer.Length);
            var frames = entry.Buffer.Length / (4 * _channels);
            accStream.Write(pcm16, 0, pcm16.Length);
            accFrames += frames;

            if (accFrames >= targetFrames)
            {
                SendAccumulated(accStream, token,
                    useTimestampZero, useTimestampBuf, bufMs, ref streamTimestamp);
                accStream.SetLength(0);
                accFrames = 0;
            }
        }
    }

    private void SendAccumulated(MemoryStream pcmStream, CancellationToken token,
        bool useTimestampZero, bool useTimestampBuf, long bufMs, ref long streamTimestamp)
    {
        var pcmData = pcmStream.ToArray();
        long timestamp;
        if (useTimestampZero)
        {
            timestamp = 0;
        }
        else
        {
            // 使用墙钟时间确保 Speaker 的 sleep = bufMs - this.d > 0
            // this.d ≈ 10-11ms（取决于采样率），bufMs 需 > this.d
            timestamp = NowMs + _clockOffsetMs + (useTimestampBuf ? bufMs : PerCallbackBufMs);
        }

        var packet = SoundSeederProtocol.BuildAudioPacket(
            _isFirstPacket, _sampleRate, _channels, 16, timestamp, pcmData);
        _isFirstPacket = false;

        if (!useTimestampZero)
            streamTimestamp += pcmData.Length / (2 * _channels) * 1000L / _sampleRate;

        WritePacket(packet, token);
    }

    private void WritePacket(byte[] packet, CancellationToken token)
    {
        if (_audioStream != null)
        {
            try
            {
                _audioStream.Write(packet, 0, packet.Length);
                _audioStream.Flush();
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "音频连接断开");
                ReconnectAudio(token);
            }
            catch (ObjectDisposedException) { }
        }
    }

    private void ReconnectAudio(CancellationToken token)
    {
        try { _audioStream?.Close(); } catch { }
        try { _audioStream?.Dispose(); } catch { }
        _audioStream = null;
        try { _audioClient?.Close(); } catch { }
        try { _audioClient?.Dispose(); } catch { }
        _audioClient = null;
        _isFirstPacket = true;
        ClearAudioQueue();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!token.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            try
            {
                _audioListener = new TcpListener(IPAddress.Any, SoundSeederProtocol.AudioPort);
                _audioListener.Start();
                _logger.LogInformation("等待 Speaker 重新连接...");

                using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                acceptCts.CancelAfter(TimeSpan.FromSeconds(10));
                _audioClient = _audioListener.AcceptTcpClientAsync(acceptCts.Token)
                    .GetAwaiter().GetResult();
                _audioStream = _audioClient.GetStream();
                _isFirstPacket = true;
                _timestampBase = NowMs + _clockOffsetMs;
                _logger.LogInformation("音频连接已重新建立");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "音频重连失败，2秒后重试");
                try { _audioClient?.Dispose(); } catch { }
                _audioClient = null;
                try { _audioListener?.Stop(); } catch { }
                _audioListener = null;
                Thread.Sleep(2000);
            }
        }
        _logger.LogWarning("音频重连超时");
    }

    public async Task StopStreamingAsync()
    {
        if (!_isRunning) return;
        await StopStreamingAsyncCore();
    }

    private async Task StopStreamingAsyncCore()
    {
        try
        {
            _streamingCts?.Cancel();

            try { _heartbeatCts?.Cancel(); } catch { }
            try { _heartbeatListener?.Stop(); } catch { }
            _heartbeatListener = null;
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;

            try { if (_controlStream != null) await SendControlAsync("$disc$"); } catch { }

            try { _controlStream?.Close(); } catch { }
            try { _controlStream?.Dispose(); } catch { }
            _controlStream = null;
            try { _controlClient?.Close(); } catch { }
            try { _controlClient?.Dispose(); } catch { }
            _controlClient = null;

            RestoreSystemMute();
            CleanupCapture();
            CleanupAudioConnection();

            _isRunning = false;
            _isFirstPacket = true;
            ClearAudioQueue();
            _generalSettingsService.EnableVirtualSpeaker = false;
            _logger.LogInformation("虚拟扬声器已停止");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止虚拟扬声器失败");
        }
    }

    private void RestoreSystemMute()
    {
        if (!_systemWasMuted) return;
        try
        {
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDevice != null)
            {
                defaultDevice.AudioEndpointVolume.Mute = false;
                _systemWasMuted = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复系统声音失败");
        }
    }

    private void CleanupCapture()
    {
        if (_capture != null)
        {
            try { _capture.StopRecording(); } catch { }
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }
    }

    private void CleanupAudioConnection()
    {
        try { _audioStream?.Close(); } catch { }
        try { _audioStream?.Dispose(); } catch { }
        _audioStream = null;
        try { _audioClient?.Close(); } catch { }
        try { _audioClient?.Dispose(); } catch { }
        _audioClient = null;
        try { _audioListener?.Stop(); } catch { }
        _audioListener = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_isRunning)
        {
            RestoreSystemMute();
            CleanupCapture();
            CleanupAudioConnection();
            _streamingCts?.Cancel();
            _streamingCts?.Dispose();
            _isRunning = false;
        }
        else
        {
            _capture?.Dispose();
            _streamingCts?.Dispose();
        }

        try { _controlStream?.Dispose(); } catch { }
        try { _controlClient?.Dispose(); } catch { }
        try { _discoveryClient?.Dispose(); } catch { }
        try { _heartbeatCts?.Dispose(); } catch { }
        try { _heartbeatListener?.Stop(); } catch { }
        _audioDataReady.Dispose();
    }
}
