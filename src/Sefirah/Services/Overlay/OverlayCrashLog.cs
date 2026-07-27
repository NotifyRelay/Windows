using System;
using System.IO;
using System.Text;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 覆盖层渲染线程崩溃日志（移植自 danmuku-kano 的 CrashLog）。
/// 渲染线程运行于独立 STA 线程，其异常难以通过常规结构化日志完整捕获，
/// 这里直接写入 %LocalAppData%\NotifyRelay\overlay-crash.log 以便排查。
/// </summary>
internal static class OverlayCrashLog
{
    private static readonly object _lock = new();

    private static string LogPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NotifyRelay");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "overlay-crash.log");
        }
    }

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.Append('[')
                  .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append("] ")
                  .AppendLine(message);
                if (ex != null)
                {
                    sb.AppendLine(ex.ToString());
                }
                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 崩溃日志本身写入失败则忽略，避免二次异常
        }
    }
}
