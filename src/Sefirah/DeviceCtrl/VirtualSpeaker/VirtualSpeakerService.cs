using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using FFMpegCore;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NotifyRelay.Data.Contracts;
using OpenSource.UPnP;

namespace NotifyRelay.DeviceCtrl.VirtualSpeaker;

public class VirtualSpeakerService : IDisposable
{
    private const int MaxAacFrames = 30;

    private readonly ILogger<VirtualSpeakerService> _logger;
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly MMDeviceEnumerator _enumerator = new();

    private WasapiLoopbackCapture? _capture;
    private CancellationTokenSource? _streamingCts;
    private TcpListener? _tcpListener;
    private Thread? _serverThread;
    private bool _isRunning;
    private bool _systemWasMuted;

    private string? _currentDeviceId;
    private string? _currentControlUrl;

    private Process? _ffmpegProcess;
    private Task? _ffmpegReadTask;
    private int _sampleRate;
    private int _channels;

    private readonly List<byte[]> _aacFrames = [];
    private readonly object _aacDataLock = new();
    private int _framesTrimmed;

    private readonly List<DlnaRendererInfo> _discoveredRenderers = [];
    private readonly object _renderersLock = new();
    private readonly Dictionary<string, string> _deviceControlUrls = [];

    public event EventHandler? StatusChanged;
    public event EventHandler<DlnaRendererInfo>? RendererDiscovered;

    public bool IsRunning => _isRunning;
    public bool SystemWasMutedByUs => _systemWasMuted;

    public VirtualSpeakerService(
        ILogger<VirtualSpeakerService> logger,
        IGeneralSettingsService generalSettingsService)
    {
        _logger = logger;
        _generalSettingsService = generalSettingsService;
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
            _logger.LogError("未选择目标DLNA设备");
            return;
        }

        if (!_deviceControlUrls.TryGetValue(deviceId, out var controlUrl) || string.IsNullOrEmpty(controlUrl))
        {
            _logger.LogError("目标DLNA设备的控制URL不可用: {DeviceId}", deviceId);
            return;
        }

        try
        {
            await EnsureFFmpegAvailableAsync();

            _streamingCts = new CancellationTokenSource();
            var token = _streamingCts.Token;

            if (_generalSettingsService.VirtualSpeakerMuteOnStart)
            {
                var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice is { AudioEndpointVolume.Mute: false })
                {
                    defaultDevice.AudioEndpointVolume.Mute = true;
                    _systemWasMuted = true;
                    _logger.LogInformation("已静音系统声音");
                }
            }

            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _channels = _capture.WaveFormat.Channels;
            _logger.LogInformation("音频捕获格式: {Rate}Hz {Channels}ch IEEE_FLOAT", _sampleRate, _channels);

            StartFFmpeg(_sampleRate, _channels);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            StartHttpServer();
            _ffmpegReadTask = Task.Run(() => ReadAacLoop(token), token);
            _capture.StartRecording();

            var localIp = GetLocalIpAddress();
            var streamUrl = $"http://{localIp}:{Constants.VirtualSpeakerHttpPort}/audio.mp3";
            _logger.LogInformation("DLNA流URL: {Url}", streamUrl);

            var didlLite = string.Format(
                "<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\"><item id=\"0\" parentID=\"-1\" restricted=\"0\"><res protocolInfo=\"http-get:*:audio/mpeg:*\">{0}</res></item></DIDL-Lite>", streamUrl);
            var escapedMetaData = System.Security.SecurityElement.Escape(didlLite);

            await SendAvTransportSoapAsync(controlUrl, "SetAVTransportURI",
                "<InstanceID>0</InstanceID>" +
                $"<CurrentURI>{streamUrl}</CurrentURI>" +
                $"<CurrentURIMetaData>{escapedMetaData}</CurrentURIMetaData>");

            await SendAvTransportSoapAsync(controlUrl, "Play",
                "<InstanceID>0</InstanceID>" +
                "<Speed>1</Speed>");

            _currentDeviceId = deviceId;
            _currentControlUrl = controlUrl;
            _isRunning = true;
            _generalSettingsService.EnableVirtualSpeaker = true;
            _logger.LogInformation("虚拟扬声器已启动，目标: {Name}", deviceName ?? deviceId);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动虚拟扬声器失败");
            _currentControlUrl = null;
            _currentDeviceId = null;
            await StopStreamingAsyncCore();
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

            try
            {
                if (!string.IsNullOrEmpty(_currentControlUrl))
                {
                    _logger.LogInformation("正在通知DLNA设备停止播放");
                    await SendAvTransportSoapAsync(_currentControlUrl, "Stop",
                        "<InstanceID>0</InstanceID>");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "通知DLNA设备停止播放失败（不影响本地清理）");
            }

            RestoreSystemMute();
            CleanupCapture();
            StopFFmpeg();
            StopHttpServer();

            if (_ffmpegReadTask != null)
            {
                try { await _ffmpegReadTask; } catch { }
                _ffmpegReadTask = null;
            }

            lock (_aacDataLock)
            {
                _aacFrames.Clear();
            }

            _isRunning = false;
            _currentControlUrl = null;
            _currentDeviceId = null;
            _generalSettingsService.EnableVirtualSpeaker = false;
            _logger.LogInformation("虚拟扬声器已停止");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止虚拟扬声器失败");
        }
    }

    private Task EnsureFFmpegAvailableAsync()
    {
        try
        {
            var path = GlobalFFOptions.GetFFMpegBinaryPath();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return Task.CompletedTask;
        }
        catch
        {
        }

        var ffmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        var ffmpegExe = Path.Combine(ffmpegDir, "ffmpeg.exe");
        if (File.Exists(ffmpegExe))
        {
            GlobalFFOptions.Configure(options => options.BinaryFolder = ffmpegDir);
            _logger.LogInformation("从内置目录加载 FFmpeg: {Path}", ffmpegExe);
            return Task.CompletedTask;
        }

        var message = $"找不到 FFmpeg，请在应用目录下创建 ffmpeg/ 文件夹并放入 ffmpeg.exe。预期路径: {ffmpegExe}";
        _logger.LogError(message);
        throw new FileNotFoundException(message);
    }

    private void StartFFmpeg(int sampleRate, int channels)
    {
        try
        {
            var ffmpegPath = GlobalFFOptions.GetFFMpegBinaryPath();
            _logger.LogInformation("FFmpeg 路径: {Path}", ffmpegPath);

            _ffmpegProcess = new Process();
            _ffmpegProcess.StartInfo.FileName = ffmpegPath;
            _ffmpegProcess.StartInfo.Arguments = $"-f f32le -ar {sampleRate} -ac {channels} " +
                "-i pipe:0 -c:a mp3 -b:a 192k -fflags +nobuffer -flags +low_delay -f mp3 pipe:1";
            _ffmpegProcess.StartInfo.RedirectStandardInput = true;
            _ffmpegProcess.StartInfo.RedirectStandardOutput = true;
            _ffmpegProcess.StartInfo.RedirectStandardError = false;
            _ffmpegProcess.StartInfo.UseShellExecute = false;
            _ffmpegProcess.StartInfo.CreateNoWindow = true;
            _ffmpegProcess.Start();
            _logger.LogInformation("FFmpeg 编码器已启动 (MP3 192kbps)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 FFmpeg 失败，请确保 ffmpeg 已安装并在系统 PATH 中");
            throw;
        }
    }

    private void StopFFmpeg()
    {
        try
        {
            if (_ffmpegProcess != null)
            {
                try { _ffmpegProcess.StandardInput.BaseStream.Close(); } catch { }
                try { _ffmpegProcess.StandardOutput.BaseStream.Close(); } catch { }
                if (!_ffmpegProcess.HasExited)
                {
                    try { _ffmpegProcess.Kill(); } catch { }
                    try { _ffmpegProcess.WaitForExit(3000); } catch { }
                }
                _ffmpegProcess.Dispose();
                _ffmpegProcess = null;
                _logger.LogInformation("FFmpeg 编码器已停止");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止 FFmpeg 进程失败");
        }
    }

    private async Task ReadAacLoop(CancellationToken token)
    {
        var buffer = new byte[65536];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var bytesRead = await _ffmpegProcess!.StandardOutput.BaseStream
                    .ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead == 0) break;

                var aacData = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, aacData, 0, bytesRead);

                lock (_aacDataLock)
                {
                    _aacFrames.Add(aacData);
                    if (_aacFrames.Count > MaxAacFrames)
                    {
                        var removeCount = _aacFrames.Count - MaxAacFrames;
                        _aacFrames.RemoveRange(0, removeCount);
                        _framesTrimmed += removeCount;
                    }
                    Monitor.PulseAll(_aacDataLock);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取 FFmpeg 输出失败");
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
                _logger.LogInformation("已恢复系统声音");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复系统声音失败");
        }
    }

    public async Task<List<DlnaRendererInfo>> DiscoverRenderersAsync(int timeoutMs = 4000)
    {
        lock (_renderersLock)
        {
            _discoveredRenderers.Clear();
            _deviceControlUrls.Clear();
        }

        var cp = new UPnPControlPoint();
        cp.OnSearch += OnSearchResponse;

        using var cts = new CancellationTokenSource(timeoutMs);
        cp.FindDeviceAsync("urn:schemas-upnp-org:device:MediaRenderer:1");

        try
        {
            await Task.Delay(timeoutMs, cts.Token);
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            cp.OnSearch -= OnSearchResponse;
        }

        lock (_renderersLock)
        {
            _logger.LogInformation("UPnP发现完成，找到 {Count} 个DLNA设备", _discoveredRenderers.Count);
            return [.. _discoveredRenderers];
        }
    }

    private void OnSearchResponse(IPEndPoint from, IPEndPoint local, Uri descriptionLocation,
        string usn, string searchTarget, int maxAge)
    {
        try
        {
            if (descriptionLocation == null)
            {
                _logger.LogDebug("UPnP搜索响应无描述URL: {Usn}", usn);
                return;
            }

            var udn = usn;
            if (udn.Contains("::"))
                udn = udn[..udn.IndexOf("::", StringComparison.Ordinal)];

            lock (_renderersLock)
            {
                if (_discoveredRenderers.Any(r => r.Id == udn))
                    return;
            }

            _logger.LogInformation("收到UPnP搜索响应: {Name} ({Usn}) at {Location}",
                searchTarget, usn, descriptionLocation);

            var descriptionXml = FetchDescriptionXml(descriptionLocation.ToString());
            if (string.IsNullOrEmpty(descriptionXml))
                return;

            var doc = XDocument.Parse(descriptionXml);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var deviceNode = doc.Descendants(ns + "device").FirstOrDefault();
            var friendlyName = deviceNode?.Element(ns + "friendlyName")?.Value ?? "Unknown";

            var controlUrl = "";
            foreach (var service in doc.Descendants(ns + "service"))
            {
                var serviceType = service.Element(ns + "serviceType")?.Value ?? "";
                var ctrlUrl = service.Element(ns + "controlURL")?.Value ?? "";

                if (!string.IsNullOrEmpty(ctrlUrl) && !ctrlUrl.StartsWith("http"))
                {
                    ctrlUrl = $"{descriptionLocation.Scheme}://{descriptionLocation.Host}:{descriptionLocation.Port}{ctrlUrl}";
                }

                if (serviceType.Contains("AVTransport"))
                {
                    controlUrl = ctrlUrl;
                }
            }

            var info = new DlnaRendererInfo
            {
                Id = udn,
                Name = friendlyName,
                Location = descriptionLocation.ToString(),
                IpAddress = descriptionLocation.Host,
                Port = descriptionLocation.Port,
                AvTransportUrl = controlUrl
            };

            lock (_renderersLock)
            {
                if (_discoveredRenderers.All(r => r.Id != info.Id))
                {
                    _discoveredRenderers.Add(info);
                    if (!string.IsNullOrEmpty(controlUrl))
                        _deviceControlUrls[udn] = controlUrl;
                    RendererDiscovered?.Invoke(this, info);
                    _logger.LogInformation("发现DLNA设备: {Name} ({Ip}:{Port})",
                        info.Name, info.IpAddress, info.Port);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "处理搜索响应失败: {Usn}", usn);
        }
    }

    private static string FetchDescriptionXml(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            return client.GetStringAsync(url).GetAwaiter().GetResult();
        }
        catch
        {
            return "";
        }
    }

    private async Task SendAvTransportSoapAsync(string controlUrl, string action, string argsXml)
    {
        try
        {
            var soapBody = $"<?xml version=\"1.0\"?>\r\n" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
                $"  <s:Body>\r\n" +
                $"    <u:{action} xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\r\n" +
                $"      {argsXml}\r\n" +
                $"    </u:{action}>\r\n" +
                $"  </s:Body>\r\n" +
                $"</s:Envelope>\r\n";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, controlUrl)
            {
                Content = new StringContent(soapBody, Encoding.UTF8, "text/xml")
            };
            request.Headers.Add("SOAPACTION", $"\"urn:schemas-upnp-org:service:AVTransport:1#{action}\"");

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("AVTransport.{Action} 调用成功", action);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AVTransport.{Action} 调用失败", action);
            throw;
        }
    }

    private void StartHttpServer()
    {
        _tcpListener = new TcpListener(IPAddress.Any, Constants.VirtualSpeakerHttpPort);
        _tcpListener.Start();
        _serverThread = new Thread(() => TcpServerLoop(_streamingCts!.Token))
        {
            IsBackground = true,
            Name = "VirtualSpeaker-HTTP"
        };
        _serverThread.Start();
        _logger.LogInformation("音频TCP服务器已启动，端口: {Port}", Constants.VirtualSpeakerHttpPort);
    }

    private void StopHttpServer()
    {
        try { _tcpListener?.Stop(); } catch { }
        _tcpListener = null;
    }

    private void TcpServerLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = _tcpListener!.AcceptTcpClient();
                _ = Task.Run(() => HandleTcpClientAsync(client, token), token);
            }
        }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TCP服务器异常");
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            client.NoDelay = true;

            using (client)
            using (var stream = client.GetStream())
            {
                var headerBuf = new byte[4096];
                var headerBytesRead = 0;
                var headerEnd = false;

                while (headerBytesRead < headerBuf.Length)
                {
                    var read = await stream.ReadAsync(headerBuf.AsMemory(headerBytesRead,
                        Math.Min(1024, headerBuf.Length - headerBytesRead)), token);
                    if (read == 0) return;
                    headerBytesRead += read;

                    var headerStr = Encoding.ASCII.GetString(headerBuf, 0, headerBytesRead);
                    if (headerStr.Contains("\r\n\r\n") || headerStr.Contains("\n\n"))
                    {
                        headerEnd = true;
                        break;
                    }
                }

                if (!headerEnd) return;
                var request = Encoding.ASCII.GetString(headerBuf, 0, headerBytesRead);
                if (!request.StartsWith("GET "))
                    return;

                var responseHeader = "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: audio/mpeg\r\n" +
                    "Transfer-Encoding: chunked\r\n" +
                    "Cache-Control: no-cache, no-store\r\n" +
                    "Connection: keep-alive\r\n" +
                    "transferMode.dlna.org: Streaming\r\n" +
                    "Content-Features: DLNA.ORG_OP=00;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000\r\n" +
                    "\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(responseHeader);
                await stream.WriteAsync(headerBytes.AsMemory(), token);

                var framesConsumed = 0;
                while (!token.IsCancellationRequested && client.Connected)
                {
                    byte[]? frame = null;
                    lock (_aacDataLock)
                    {
                        var relativeIndex = framesConsumed - _framesTrimmed;
                        if (relativeIndex < 0)
                        {
                            framesConsumed = _framesTrimmed;
                            relativeIndex = 0;
                        }

                        if (relativeIndex < _aacFrames.Count)
                        {
                            frame = _aacFrames[relativeIndex];
                            framesConsumed++;
                        }
                        else
                        {
                            Monitor.Wait(_aacDataLock, 10);
                        }
                    }

                    if (frame != null)
                    {
                        var chunkSizeHex = frame.Length.ToString("X");
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(chunkSizeHex + "\r\n"), token);
                        await stream.WriteAsync(frame.AsMemory(), token);
                        await stream.WriteAsync(new byte[] { (byte)'\r', (byte)'\n' }, token);
                    }
                }

                if (client.Connected)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"), token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "客户端连接处理异常");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            _ffmpegProcess?.StandardInput.BaseStream.Write(e.Buffer, 0, e.BytesRecorded);
            _ffmpegProcess?.StandardInput.BaseStream.Flush();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "写入FFmpeg管道失败");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _logger.LogInformation("音频捕获已停止");
    }

    private void CleanupCapture()
    {
        if (_capture != null)
        {
            try { _capture.StopRecording(); }
            catch { }
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }
    }

    private static string GetLocalIpAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        return host.AddressList.FirstOrDefault(
            ip => ip.AddressFamily == AddressFamily.InterNetwork
                  && !IPAddress.IsLoopback(ip))?.ToString() ?? "127.0.0.1";
    }

    public void Dispose()
    {
        if (_isRunning)
        {
            RestoreSystemMute();
            CleanupCapture();
            StopFFmpeg();
            StopHttpServer();
            _streamingCts?.Cancel();
            _streamingCts?.Dispose();
            _isRunning = false;
        }
        else
        {
            _capture?.Dispose();
            _streamingCts?.Dispose();
            StopFFmpeg();
        }
    }
}
