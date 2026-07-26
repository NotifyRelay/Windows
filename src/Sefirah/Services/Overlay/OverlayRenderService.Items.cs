using NotifyRelay.Models.Render;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    public void ShowDanmaku(string appName, string title, string body, byte[]? iconPng, string deviceName)
    {
        var text = string.IsNullOrEmpty(appName) ? $"{title} - {body}" : $"{appName}: {title} - {body}";

        lock (_lock)
        {
            var item = new DanmakuItem
            {
                Text = text,
                IconPng = iconPng,
                Settings = _currentStyle,
                StartTime = Stopwatch.GetTimestamp(),
                AppName = appName,
                DeviceName = deviceName
            };
            AssignTrack(item);
            _items.Add(item);
        }
    }

    public void ShowMediaCard(string deviceId, string deviceName, string title, string artist, byte[]? coverPng, bool isPlaying)
    {
        lock (_lock)
        {
            var existing = _items.OfType<MediaCardItem>().FirstOrDefault(m => m.DeviceId == deviceId);
            if (existing != null)
            {
                // 空值表示"未改变"，仅更新有实际值的字段
                bool titleChanged = false;
                bool artistChanged = false;
                if (!string.IsNullOrEmpty(title) && title != existing.Title)
                {
                    existing.Title = title;
                    existing.TitleLayout?.Dispose();
                    existing.TitleLayout = null;
                    titleChanged = true;
                }
                if (!string.IsNullOrEmpty(artist) && artist != existing.Artist)
                {
                    existing.Artist = artist;
                    existing.ArtistLayout?.Dispose();
                    existing.ArtistLayout = null;
                    artistChanged = true;
                }
                if (coverPng != null)
                {
                    existing.CoverPng = coverPng;
                    existing.CoverBitmap?.Dispose();
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
            _items.Add(item);
        }
    }

    public void RemoveMediaCard(string deviceId)
    {
        lock (_lock)
        {
            var item = _items.OfType<MediaCardItem>().FirstOrDefault(m => m.DeviceId == deviceId);
            if (item != null)
            {
                item.Active = false;
                item.Dispose();
                _items.Remove(item);
            }
        }
    }

    public void ShowSuperIsland(string sourceId, string deviceName, SuperIslandState state)
    {
        lock (_lock)
        {
            var existing = _items.OfType<SuperIslandItem>().FirstOrDefault(s => s.SourceId == sourceId);
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

                // 触发 UI 刷新：使缓存的 Layout 失效
                existing.TitleLayout?.Dispose();
                existing.TitleLayout = null;
                existing.SubtitleLayout?.Dispose();
                existing.SubtitleLayout = null;
                existing.AdditionalTextLayout?.Dispose();
                existing.AdditionalTextLayout = null;
                existing.ExtraLayout?.Dispose();
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
            _items.Add(item);
        }
    }

    public void RemoveSuperIsland(string sourceId)
    {
        lock (_lock)
        {
            var item = _items.OfType<SuperIslandItem>().FirstOrDefault(s => s.SourceId == sourceId);
            if (item != null)
            {
                item.Active = false;
                item.Dispose();
                _items.Remove(item);
            }
        }
    }

    public void UpdateStyle(DanmakuStyleSettings settings)
    {
        lock (_lock)
        {
            _currentStyle = settings;
        }
    }
}
