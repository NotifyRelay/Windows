using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace NotifyRelay.Platforms.Windows.Services;

/// <summary>
/// SMTC 会话集合的登记与事件订阅。
/// 负责发现/移除系统媒体会话，并把会话级事件以本类的强类型事件向外转发。
/// </summary>
public class SmtcSessionRegistry(ILogger<SmtcSessionRegistry> logger)
{
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> activeSessions = [];
    private GlobalSystemMediaTransportControlsSessionManager? manager;

    /// <summary>当前登记的会话数量。</summary>
    public int Count
    {
        get
        {
            lock (activeSessions)
            {
                return activeSessions.Count;
            }
        }
    }

    /// <summary>系统当前活动的媒体会话（无则为 null）。</summary>
    public GlobalSystemMediaTransportControlsSession? CurrentSession => manager?.GetCurrentSession();

    /// <summary>会话移除（退订事件后触发）。</summary>
    public event EventHandler<string>? SessionRemoved;

    /// <summary>媒体属性变更。</summary>
    public event EventHandler<GlobalSystemMediaTransportControlsSession>? MediaPropertiesChanged;

    /// <summary>播放状态变更。</summary>
    public event EventHandler<GlobalSystemMediaTransportControlsSession>? PlaybackInfoChanged;

    /// <summary>
    /// 请求会话管理器、完成首次会话同步并开始监听会话增减。
    /// </summary>
    /// <returns>会话管理器获取成功返回 true；失败返回 false（调用方应中止后续初始化）。</returns>
    public async Task<bool> InitializeAsync()
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (manager is null)
        {
            logger.LogError("初始化系统媒体传输控制会话管理器失败");
            return false;
        }

        SyncSessions();
        manager.SessionsChanged += SessionsChanged;
        return true;
    }

    /// <summary>指定 AUMID 的会话是否已登记。</summary>
    public bool Contains(string appUserModelId)
    {
        lock (activeSessions)
        {
            return activeSessions.ContainsKey(appUserModelId);
        }
    }

    /// <summary>按 AUMID 查找已登记的会话。</summary>
    public bool TryGetBySource(string source, out GlobalSystemMediaTransportControlsSession? session)
    {
        lock (activeSessions)
        {
            return activeSessions.TryGetValue(source, out session);
        }
    }

    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager manager, SessionsChangedEventArgs args)
    {
        SyncSessions();
    }

    private void SyncSessions()
    {
        if (manager is null) return;

        try
        {
            UpdateSessionsList(manager.GetSessions());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新活动会话时出错");
        }
    }

    private void UpdateSessionsList(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        lock (activeSessions)
        {
            var currentSessionIds = new HashSet<string>(sessions.Select(s => s.SourceAppUserModelId));

            foreach (var sessionId in activeSessions.Keys.ToList())
            {
                if (!currentSessionIds.Contains(sessionId))
                {
                    RemoveSession(sessionId);
                }
            }

            foreach (var session in sessions)
            {
                if (!activeSessions.ContainsKey(session.SourceAppUserModelId))
                {
                    AddSession(session);
                }
            }
        }
    }

    private void RemoveSession(string sessionId)
    {
        if (activeSessions.TryGetValue(sessionId, out var session))
        {
            activeSessions.Remove(sessionId);
            UnsubscribeFromSessionEvents(session);
            SessionRemoved?.Invoke(this, sessionId);
        }
    }

    private void AddSession(GlobalSystemMediaTransportControlsSession session)
    {
        if (!activeSessions.ContainsKey(session.SourceAppUserModelId))
        {
            activeSessions[session.SourceAppUserModelId] = session;
            SubscribeToSessionEvents(session);
        }
    }

    private void SubscribeToSessionEvents(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
        session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
    }

    private void UnsubscribeFromSessionEvents(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
        session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
    }

    private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        try
        {
            MediaPropertiesChanged?.Invoke(this, sender);
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常（媒体属性变更）：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新播放数据时出错：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
    }

    private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        try
        {
            PlaybackInfoChanged?.Invoke(this, sender);
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常（播放信息变更）：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新播放数据时出错：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
    }
}
