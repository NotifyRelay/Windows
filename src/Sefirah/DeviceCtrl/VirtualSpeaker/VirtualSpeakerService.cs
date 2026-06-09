using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NotifyRelay.Data.Contracts;
using OpenSource.UPnP;

namespace NotifyRelay.DeviceCtrl.VirtualSpeaker;

public class VirtualSpeakerService : IDisposable
{
    private readonly ILogger<VirtualSpeakerService> _logger;
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly MMDeviceEnumerator _enumerator = new();

    private WasapiLoopbackCapture? _capture;
    private BufferedWaveProvider? _audioBuffer;
    private CancellationTokenSource? _streamingCts;
    private TcpListener? _tcpListener;
    private Thread? _serverThread;
    private bool _isRunning;
    private bool _systemWasMuted;

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
            _streamingCts = new CancellationTokenSource();

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
            _audioBuffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true
            };
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            StartHttpServer();
            _capture.StartRecording();

            var localIp = GetLocalIpAddress();
            var streamUrl = $"http://{localIp}:{Constants.VirtualSpeakerHttpPort}/audio.wav";
            _logger.LogInformation("DLNA流URL: {Url}", streamUrl);

            await SendAvTransportSoapAsync(controlUrl, "SetAVTransportURI",
                "<InstanceID>0</InstanceID>" +
                $"<CurrentURI>{streamUrl}</CurrentURI>" +
                "<CurrentURIMetaData></CurrentURIMetaData>");

            await SendAvTransportSoapAsync(controlUrl, "Play",
                "<InstanceID>0</InstanceID>" +
                "<Speed>1</Speed>");

            _isRunning = true;
            _generalSettingsService.EnableVirtualSpeaker = true;
            _logger.LogInformation("虚拟扬声器已启动，目标: {Name}", deviceName ?? deviceId);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动虚拟扬声器失败");
            await StopStreaming();
        }
    }

    public Task StopStreaming()
    {
        if (!_isRunning) return Task.CompletedTask;

        try
        {
            _streamingCts?.Cancel();

            if (_systemWasMuted)
            {
                var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice != null)
                {
                    defaultDevice.AudioEndpointVolume.Mute = false;
                    _systemWasMuted = false;
                    _logger.LogInformation("已恢复系统声音");
                }
            }

            CleanupCapture();
            StopHttpServer();
            _isRunning = false;
            _generalSettingsService.EnableVirtualSpeaker = false;
            _logger.LogInformation("虚拟扬声器已停止");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止虚拟扬声器失败");
        }

        return Task.CompletedTask;
    }

    public List<DlnaRendererInfo> DiscoverRenderers(int timeoutMs = 4000)
    {
        lock (_renderersLock)
        {
            _discoveredRenderers.Clear();
            _deviceControlUrls.Clear();
        }

        var cp = new UPnPControlPoint();
        cp.OnSearch += OnSearchResponse;

        cp.FindDeviceAsync("urn:schemas-upnp-org:device:MediaRenderer:1");
        Thread.Sleep(timeoutMs);
        cp.OnSearch -= OnSearchResponse;

        lock (_renderersLock)
        {
            _logger.LogInformation("UPnP发现完成，找到 {Count} 个DLNA设备", _discoveredRenderers.Count);
            return [.. _discoveredRenderers];
        }
    }

    public Task<List<DlnaRendererInfo>> DiscoverRenderersAsync(int timeoutMs = 4000)
    {
        return Task.Run(() => DiscoverRenderers(timeoutMs));
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
            var content = new StringContent(soapBody, Encoding.UTF8, "text/xml");
            content.Headers.Clear();
            content.Headers.Add("SOAPACTION", $"\"urn:schemas-upnp-org:service:AVTransport:1#{action}\"");

            var response = await client.PostAsync(controlUrl, content);
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
            using (client)
            using (var stream = client.GetStream())
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

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
                    "Content-Type: audio/wav\r\n" +
                    "Cache-Control: no-cache\r\n" +
                    "Connection: close\r\n" +
                    "\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(responseHeader);
                await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), token);

                var format = _capture?.WaveFormat;
                if (format == null) return;

                var wavHeader = CreateWavHeader(format.SampleRate, format.BitsPerSample, format.Channels);
                await stream.WriteAsync(wavHeader.AsMemory(0, wavHeader.Length), token);
                await stream.FlushAsync(token);

                var chunkSize = format.AverageBytesPerSecond / 50;
                var buf = new byte[Math.Max(chunkSize, 1024)];

                while (!token.IsCancellationRequested && client.Connected)
                {
                    var bytesRead = _audioBuffer?.Read(buf, 0, buf.Length) ?? 0;
                    if (bytesRead > 0)
                    {
                        await stream.WriteAsync(buf.AsMemory(0, bytesRead), token);
                        await stream.FlushAsync(token);
                    }
                    else
                    {
                        await Task.Delay(10, token);
                    }
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

    private static byte[] CreateWavHeader(int sampleRate, int bitsPerSample, int channels)
    {
        using var ms = new MemoryStream(44);
        using var writer = new BinaryWriter(ms, Encoding.ASCII);
        writer.Write("RIFF".ToCharArray());
        writer.Write(int.MaxValue);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write((short)bitsPerSample);
        writer.Write("data".ToCharArray());
        writer.Write(int.MaxValue);
        return ms.ToArray();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _audioBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
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
        _ = StopStreaming();
        _capture?.Dispose();
        _streamingCts?.Dispose();
        StopHttpServer();
    }
}
