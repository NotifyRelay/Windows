using System.Net;
using System.Net.Sockets;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NotifyRelay.Data.Contracts;

namespace NotifyRelay.DeviceCtrl.VirtualSpeaker;

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
    private readonly object _audioDataLock = new();
    private byte[]? _pendingAudioData;
    private int _pendingAudioLength;

    private bool _isRunning;
    private bool _isDisposed;
    private bool _systemWasMuted;
    private string? _playerUuid;
    private int _sampleRate;
    private int _channels;
    private bool _isFirstPacket = true;

    // 积累发送：累积 500ms WASAI 数据后一次性发，所有包 timestamp=0 跳过 Speaker 时序计算
    private const int TargetPacketMs = 500;

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

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        lock (_audioDataLock)
        {
            _pendingAudioData = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, _pendingAudioData, 0, e.BytesRecorded);
            _pendingAudioLength = e.BytesRecorded;
            _audioDataReady.Set();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _logger.LogInformation("音频捕获已停止");
    }

    private void AudioWriterLoop(CancellationToken token)
    {
        var accumulatedPcmStream = new MemoryStream();
        long accumulatedFrames = 0;
        var targetFrames = _sampleRate * TargetPacketMs / 1000;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_audioDataReady.WaitOne(100))
                {
                    if (accumulatedPcmStream.Length > 0)
                    {
                        FlushAccumulatedPacket(accumulatedPcmStream, accumulatedFrames, token);
                        accumulatedPcmStream.SetLength(0);
                        accumulatedFrames = 0;
                    }
                    continue;
                }

                byte[]? floatBuffer;
                int bytesRecorded;
                lock (_audioDataLock)
                {
                    if (_pendingAudioData == null) continue;
                    floatBuffer = _pendingAudioData;
                    bytesRecorded = _pendingAudioLength;
                    _pendingAudioData = null;
                }

                var pcm16 = SoundSeederProtocol.Float32ToPcm16(floatBuffer, bytesRecorded);
                var frames = bytesRecorded / (4 * _channels);
                accumulatedPcmStream.Write(pcm16, 0, pcm16.Length);
                accumulatedFrames += frames;

                if (accumulatedFrames >= targetFrames)
                {
                    FlushAccumulatedPacket(accumulatedPcmStream, accumulatedFrames, token);
                    accumulatedPcmStream.SetLength(0);
                    accumulatedFrames = 0;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "音频写入异常");
            }
        }
    }

    private void FlushAccumulatedPacket(MemoryStream pcmStream, long frames, CancellationToken token)
    {
        var pcmData = pcmStream.ToArray();
        // 所有包用 timestamp=0 跳过 Speaker 时序计算
        // 仅第一包 isReset=true 初始化 SourceDataLine
        var packet = SoundSeederProtocol.BuildAudioPacket(
            _isFirstPacket, _sampleRate, _channels, 16, 0, pcmData);
        _isFirstPacket = false;

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
