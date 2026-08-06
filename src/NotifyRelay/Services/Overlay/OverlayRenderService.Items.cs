using NotifyRelay.Models.Render;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    public void ShowDanmaku(string appName, string title, string body, byte[]? iconPng, string deviceName)
    {
        var text = string.IsNullOrEmpty(appName) ? $"{title} - {body}" : $"{appName}: {title} - {body}";
        // 展平换行，保证单行滚动
        text = text.Replace("\r", " ").Replace("\n", " ");

        DanmakuStyleSettings style;
        if (Monitor.TryEnter(_lock, 2000))
        {
            try { style = _currentStyle; }
            finally { Monitor.Exit(_lock); }
        }
        else
        {
            // 渲染线程异常持锁超时：退化为直接引用读取（引用赋值原子），弹幕仍可入队显示
            style = _currentStyle;
        }

        // 入队，由渲染线程按多屏模式分发到各屏覆盖层
        _requests.Enqueue(new DanmakuRequest
        {
            Text = text,
            IconPng = iconPng,
            Settings = style
        });
    }

    public void ShowMediaCard(string deviceId, string deviceName, string title, string artist, byte[]? coverPng, bool isPlaying)
    {
        // 有界等待：渲染线程异常持锁超时则跳过本次更新，避免业务线程无限阻塞拖死应用
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过媒体卡片更新");
            return;
        }
        try
        {
            var existing = _topItems.OfType<MediaCardItem>().FirstOrDefault(m => m.DeviceId == deviceId);
            if (existing != null)
            {
                // 空值表示"未改变"，仅更新有实际值的字段
                bool titleChanged = false;
                bool artistChanged = false;
                if (!string.IsNullOrEmpty(title) && title != existing.Title)
                {
                    existing.Title = title;
                    DeferDispose(existing.TitleLayout);
                    existing.TitleLayout = null;
                    titleChanged = true;
                }
                if (!string.IsNullOrEmpty(artist) && artist != existing.Artist)
                {
                    existing.Artist = artist;
                    DeferDispose(existing.ArtistLayout);
                    existing.ArtistLayout = null;
                    artistChanged = true;
                }
                if (coverPng != null)
                {
                    existing.CoverPng = coverPng;
                    DeferDispose(existing.CoverBitmap);
                    existing.CoverBitmap = null;
                }
                bool playingChanged = existing.IsPlaying != isPlaying;
                existing.IsPlaying = isPlaying;
                if (titleChanged || playingChanged)
                {
                    existing.MarqueeAnchorTime = Stopwatch.GetTimestamp();
                }
                existing.LastUpdateTime = Stopwatch.GetTimestamp();

                // title 且 artist 都变更时才触发展开（新曲目切换）
                if (titleChanged && artistChanged)
                {
                    existing.IsExpanded = true;
                    existing.ExpandedSince = Stopwatch.GetTimestamp();
                }
                return;
            }

            var now = Stopwatch.GetTimestamp();
            var item = new MediaCardItem
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                Title = title,
                Artist = artist,
                CoverPng = coverPng,
                IsPlaying = isPlaying,
                StartTime = now,
                LastUpdateTime = now,
                MarqueeAnchorTime = now,
                IsExpanded = true,
                ExpandedSince = now
            };
            _topItems.Add(item);
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    public void RemoveMediaCard(string deviceId)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过媒体卡片移除");
            return;
        }
        try
        {
            var item = _topItems.OfType<MediaCardItem>().FirstOrDefault(m => m.DeviceId == deviceId);
            if (item != null)
            {
                item.Active = false;
                DeferDispose(item);
                _topItems.Remove(item);
            }
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    public void ShowSuperIsland(string sourceId, string deviceName, SuperIslandState state)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过 SuperIsland 更新");
            return;
        }
        try
        {
            var existing = _topItems.OfType<SuperIslandItem>().FirstOrDefault(s => s.SourceId == sourceId);
            if (existing != null)
            {
                // 处理增量变更合并
                if (!string.IsNullOrEmpty(state.ChangesJson))
                {
                    existing.State.MergeChanges(state.ChangesJson);
                }

                // 空值表示"未改变"，仅合并有实际值的字段
                if (!string.IsNullOrEmpty(state.Title)) existing.State.Title = state.Title;
                if (!string.IsNullOrEmpty(state.Subtitle)) existing.State.Subtitle = state.Subtitle;
                if (!string.IsNullOrEmpty(state.Extra)) existing.State.Extra = state.Extra;
                if (state.IconPng != null) existing.State.IconPng = state.IconPng;
                if (state.Pics != null) existing.State.Pics = state.Pics;
                if (state.Progress > 0) existing.State.Progress = state.Progress;
                if (state.TimerType != TimerType.None) existing.State.TimerType = state.TimerType;
                if (state.TimerValue > 0) existing.State.TimerValue = state.TimerValue;
                if (state.TimerStartTime > 0) existing.State.TimerStartTime = state.TimerStartTime;
                if (!string.IsNullOrEmpty(state.ParamV2Raw))
                {
                    existing.State.ParamV2Raw = state.ParamV2Raw;
                    SuperIslandParamV2Parser.ApplyToState(existing.State, state.ParamV2Raw);
                }

                existing.LastUpdateTime = Stopwatch.GetTimestamp();

                // 触发 UI 刷新：使缓存的 Layout 失效（延迟释放，由渲染线程统一执行）
                DeferDispose(existing.TitleLayout);
                existing.TitleLayout = null;
                DeferDispose(existing.SubtitleLayout);
                existing.SubtitleLayout = null;
                DeferDispose(existing.AdditionalTextLayout);
                existing.AdditionalTextLayout = null;
                DeferDispose(existing.ExtraLayout);
                existing.ExtraLayout = null;

                // Extra 变更时重新展开
                if (!string.IsNullOrEmpty(state.Extra))
                {
                    existing.IsExpanded = true;
                    existing.ExpandedSince = Stopwatch.GetTimestamp();
                }
                return;
            }

            var item = new SuperIslandItem
            {
                SourceId = sourceId,
                DeviceName = deviceName,
                State = state,
                IconPng = state.IconPng,
                StartTime = Stopwatch.GetTimestamp(),
                LastUpdateTime = Stopwatch.GetTimestamp(),
                IsExpanded = true,
                ExpandedSince = Stopwatch.GetTimestamp()
            };
            _topItems.Add(item);
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    public void RemoveSuperIsland(string sourceId)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过 SuperIsland 移除");
            return;
        }
        try
        {
            var item = _topItems.OfType<SuperIslandItem>().FirstOrDefault(s => s.SourceId == sourceId);
            if (item != null)
            {
                item.Active = false;
                DeferDispose(item);
                _topItems.Remove(item);
            }
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    public void UpdateStyle(DanmakuStyleSettings settings)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过样式更新");
            return;
        }
        try
        {
            _currentStyle = settings;
            // 性能档位：0=流畅(跟随刷新率) 1=均衡(≤60FPS) 2=游戏(≤30FPS)
            _maxFps = settings.PerformanceMode switch
            {
                1 => 60,
                2 => 30,
                _ => 0
            };
            // 多屏模式变化时，触发覆盖层重建
            if (settings.DisplayScreenMode != _screenMode)
            {
                _displayDirty = true;
            }
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>使顶部卡片的设备相关资源失效（覆盖层重建时调用）。</summary>
    private void InvalidateTopItemDeviceResources()
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            // 获取超时则跳过：覆盖层重建后渲染线程会按需懒加载资源，不影响正确性
            _logger.LogWarning("覆盖层数据锁获取超时，跳过顶部卡片资源失效");
            return;
        }
        try
        {
            foreach (var it in _topItems)
            {
                if (it is MediaCardItem m)
                {
                    m.CoverBitmap?.Dispose(); m.CoverBitmap = null;
                    m.TitleLayout?.Dispose(); m.TitleLayout = null;
                    m.ArtistLayout?.Dispose(); m.ArtistLayout = null;
                }
                else if (it is SuperIslandItem s)
                {
                    s.IconBitmap?.Dispose(); s.IconBitmap = null;
                    s.TitleLayout?.Dispose(); s.TitleLayout = null;
                    s.SubtitleLayout?.Dispose(); s.SubtitleLayout = null;
                    s.AdditionalTextLayout?.Dispose(); s.AdditionalTextLayout = null;
                    s.ExtraLayout?.Dispose(); s.ExtraLayout = null;
                }
            }
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }
}
