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
    private long _streamTimestamp;

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
            await _discoveryClient.SendAsync(probeBytes, probeBytes.Length,
                SoundSeederProtocol.MulticastAddress, SoundSeederProtocol.MulticastListenPort);
            _logger.LogInformation("已发送发现探测包");

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

            await Task.Delay(timeoutMs, token);
            _discoveryCts.Cancel();

            try { await listenTask; } catch { }

            lock (_speakersLock)
            {
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
        _streamTimestamp = 0;

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

            StartAudioListener();

            await SendControlAsync("$setPlayer$",
                $"[\"{_playerUuid}\",\"{Dns.GetHostName()}\",{SoundSeederProtocol.PlayerVersion}]");
            await SendControlAsync("$idP$", _playerUuid);
            await SendControlAsync("$setv$", "15");
            await SendControlAsync("$setch$", "1");
            await SendControlAsync("$setOffM$", "0");

            await SendControlAsync("$con$");
            _logger.LogInformation("已请求音频连接");

            if (!await WaitForAudioConnectionAsync(token, TimeSpan.FromSeconds(20)))
            {
                _logger.LogError("扬声器未在超时内连接音频通道。请检查：1) Windows防火墙是否阻止了5353端口入站连接 2) 扬声器设备是否与PC在同一网络 3) 扬声器音频通道端口是否非默认");
                await StopStreamingAsyncCore();
                return;
            }
            _logger.LogInformation("音频通道已建立");

            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _channels = _capture.WaveFormat.Channels;
            _logger.LogInformation("音频捕获格式: {Rate}Hz {Channels}ch", _sampleRate, _channels);

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

    private async Task<bool> WaitForAudioConnectionAsync(CancellationToken token, TimeSpan timeout)
    {
        if (_audioListener == null) return false;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        linkedCts.CancelAfter(timeout);

        try
        {
            var acceptTask = _audioListener.AcceptTcpClientAsync();

            using var reg = linkedCts.Token.Register(() =>
            {
                try { _audioListener?.Stop(); } catch { }
            });

            _audioClient = await acceptTask;
            _audioStream = _audioClient.GetStream();
            return true;
        }
        catch (SocketException) { return false; }
        catch (ObjectDisposedException) { return false; }
        catch (OperationCanceledException) { return false; }
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

    private void StartAudioListener()
    {
        _audioListener = new TcpListener(IPAddress.Any, SoundSeederProtocol.AudioPort);
        _audioListener.Start();
        _logger.LogInformation("音频监听器已启动，端口: {Port}", SoundSeederProtocol.AudioPort);
    }

    private void StopAudioListener()
    {
        try { _audioStream?.Close(); } catch { }
        try { _audioStream?.Dispose(); } catch { }
        _audioStream = null;

        try { _audioClient?.Close(); } catch { }
        try { _audioClient?.Dispose(); } catch { }
        _audioClient = null;

        try { _audioListener?.Stop(); } catch { }
        try { _audioListener?.Dispose(); } catch { }
        _audioListener = null;
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
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_audioDataReady.WaitOne(100)) continue;

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
                var samples = bytesRecorded / 4;

                var packet = SoundSeederProtocol.BuildAudioPacket(
                    _isFirstPacket, _sampleRate, _channels, 16,
                    _streamTimestamp, pcm16);
                _isFirstPacket = false;
                _streamTimestamp += samples * 1000L / _sampleRate;

                if (_audioStream != null)
                {
                    try
                    {
                        _audioStream.Write(packet, 0, packet.Length);
                        _audioStream.Flush();
                    }
                    catch (IOException)
                    {
                        _logger.LogWarning("音频连接断开，等待重连...");
                        WaitForAudioReconnection(token);
                    }
                    catch (ObjectDisposedException) { break; }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "音频写入异常");
            }
        }
    }

    private void WaitForAudioReconnection(CancellationToken token)
    {
        try { _audioStream?.Close(); } catch { }
        try { _audioStream?.Dispose(); } catch { }
        _audioStream = null;
        try { _audioClient?.Close(); } catch { }
        try { _audioClient?.Dispose(); } catch { }
        _audioClient = null;
        _isFirstPacket = true;

        if (_audioListener == null) return;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var acceptTask = _audioListener.AcceptTcpClientAsync();

            using var reg = linkedCts.Token.Register(() =>
            {
                try { _audioListener?.Stop(); } catch { }
            });

            _audioClient = acceptTask.GetAwaiter().GetResult();
            _audioStream = _audioClient.GetStream();
            _logger.LogInformation("音频连接已重新建立");
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "等待音频重连异常");
        }
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

            try { if (_controlStream != null) await SendControlAsync("$disc$"); } catch { }

            try { _controlStream?.Close(); } catch { }
            try { _controlStream?.Dispose(); } catch { }
            _controlStream = null;
            try { _controlClient?.Close(); } catch { }
            try { _controlClient?.Dispose(); } catch { }
            _controlClient = null;

            RestoreSystemMute();
            CleanupCapture();
            StopAudioListener();

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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_isRunning)
        {
            RestoreSystemMute();
            CleanupCapture();
            StopAudioListener();
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
        _audioDataReady.Dispose();
    }
}
