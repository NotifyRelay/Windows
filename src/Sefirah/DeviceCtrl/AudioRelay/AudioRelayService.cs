using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NotifyRelay.Native;

namespace NotifyRelay.DeviceCtrl.AudioRelay;

public class AudioRelayService : IDisposable
{
    private readonly ILogger<AudioRelayService> _logger;

    private WasapiLoopbackCapture? _capture;
    private WaveOut? _waveOut;
    private BufferedWaveProvider? _waveProvider;
    private CancellationTokenSource? _cts;

    private bool _isRunning;
    private bool _isDisposed;

    public bool IsActive => _isRunning;
    public event EventHandler? StatusChanged;

    private NotifyRelayCore.AudioDataCallback? _dataCb;
    private NotifyRelayCore.AudioEventCallback? _eventCb;

    public AudioRelayService(ILogger<AudioRelayService> logger)
    {
        _logger = logger;
    }

    public bool CanStart =>
        NativeCore.Context != IntPtr.Zero &&
        NativeCore.AudioIsActive() == 0;

    public Task StartSendAsync(string remoteUuid, string remoteIp, int sampleRate = 48000, int channels = 2)
    {
        if (_isRunning)
        {
            _logger.LogWarning("音频中继已在运行中");
            return Task.CompletedTask;
        }

        _logger.LogInformation("音频中继: 启动发送模式, 远端UUID={RemoteUuid}, 远端IP={RemoteIp}", remoteUuid, remoteIp);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var result = NativeCore.AudioStart("send", "", sampleRate, channels, remoteUuid);
            if (result != 0)
            {
                _logger.LogError("音频中继: AudioStart(send) 返回 {Result}", result);
                return Task.CompletedTask;
            }

            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += (s, e) => OnCaptureDataAvailable(e, token);
            _capture.RecordingStopped += (s, e) =>
            {
                _logger.LogInformation("音频中继: 捕获已停止");
                StopInternal();
            };

            _capture.StartRecording();
            _isRunning = true;
            _logger.LogInformation("音频中继: 发送模式已启动, 捕获格式: {Rate}Hz {Channels}ch",
                _capture.WaveFormat.SampleRate, _capture.WaveFormat.Channels);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "音频中继: 启动发送模式失败");
            StopInternal();
        }

        return Task.CompletedTask;
    }

    public Task StartReceiveAsync(string remoteUuid, string deviceIp, int sampleRate = 48000, int channels = 2)
    {
        if (_isRunning)
        {
            _logger.LogWarning("音频中继已在运行中");
            return Task.CompletedTask;
        }

        _logger.LogInformation("音频中继: 启动接收模式, 远端UUID={RemoteUuid}, IP={DeviceIp}", remoteUuid, deviceIp);

        _cts = new CancellationTokenSource();

        try
        {
            var channelConfig = channels == 2 ? 2 : 1;
            var waveFormat = new WaveFormat(sampleRate, 16, channelConfig);
            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true
            };

            _waveOut = new WaveOut
            {
                DesiredLatency = 200,
                NumberOfBuffers = 3
            };
            _waveOut.Init(_waveProvider);
            _waveOut.Play();

            _dataCb = (deviceUuid, pcmData, pcmLen, sr, ch, userData) =>
            {
                if (pcmData == IntPtr.Zero || pcmLen <= 0) return;
                try
                {
                    var buffer = new byte[pcmLen];
                    Marshal.Copy(pcmData, buffer, 0, pcmLen);
                    _waveProvider?.AddSamples(buffer, 0, pcmLen);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "音频中继: 写入播放缓冲区失败");
                }
            };
            _eventCb = (deviceUuid, eventStr, errorMsg, userData) =>
            {
                var evt = Marshal.PtrToStringUTF8(eventStr) ?? "null";
                var err = Marshal.PtrToStringUTF8(errorMsg) ?? "";
                _logger.LogDebug("音频中继: 事件={Event}, 错误={Error}", evt, err);
            };

            NativeCore.RegisterAudioCallbacks(_dataCb, _eventCb);

            var result = NativeCore.AudioStart("recv", deviceIp, sampleRate, channels, remoteUuid);
            if (result != 0)
            {
                _logger.LogError("音频中继: AudioStart(recv) 返回 {Result}", result);
                CleanupPlayback();
                return Task.CompletedTask;
            }

            _isRunning = true;
            _logger.LogInformation("音频中继: 接收模式已启动");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "音频中继: 启动接收模式失败");
            StopInternal();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;
        _logger.LogInformation("音频中继: 停止");
        StopInternal();
        return Task.CompletedTask;
    }

    private void StopInternal()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { }

        try
        {
            if (_isRunning || NativeCore.AudioIsActive() != 0)
            {
                NativeCore.AudioStop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "音频中继: AudioStop 异常");
        }

        CleanupCapture();
        CleanupPlayback();

        _isRunning = false;
        _logger.LogInformation("音频中继: 已停止");
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCaptureDataAvailable(WaveInEventArgs e, CancellationToken token)
    {
        if (token.IsCancellationRequested || e.BytesRecorded == 0) return;

        var pcm16 = Float32ToPcm16(e.Buffer, e.BytesRecorded);
        try
        {
            NativeCore.AudioWriteFrame(pcm16);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "音频中继: 写入 PCM 帧失败");
        }
    }

    private static byte[] Float32ToPcm16(byte[] floatBuffer, int bytesRecorded)
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

    private void CleanupCapture()
    {
        if (_capture != null)
        {
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }
    }

    private void CleanupPlayback()
    {
        if (_waveOut != null)
        {
            try { _waveOut.Stop(); } catch { }
            _waveOut.Dispose();
            _waveOut = null;
        }
        _waveProvider = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopInternal();
        _cts?.Dispose();
    }
}
