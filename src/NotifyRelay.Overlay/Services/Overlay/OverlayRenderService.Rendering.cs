using NotifyRelay.Models.Render;
using NotifyRelay.Services.Overlay.UI;
using NotifyRelay.Services.Overlay.UI.Islands;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 顶部卡片（媒体 + 超级岛）的渲染编排：锁内快照与超时移除，锁外交给声明式运行时
/// Compose → Measure → Place → Paint。卡片间距由实测高度自然累加（修正旧实现固定 y 步进）。
/// </summary>
public partial class OverlayRenderService
{
    /// <summary>顶部卡片距屏幕顶部的固定偏移（等价旧实现 y 起点 10）。</summary>
    private const float TopCardsOffsetY = 10f;

    private void RenderTopCards(ScreenOverlay overlay, double now, double freq)
    {
        // 锁内仅做轻量快照与超时移除；资源加载与绘制移到锁外，避免渲染线程长时间持锁
        List<MediaCardItem> mediaItems;
        List<SuperIslandItem> superItems;
        if (Monitor.TryEnter(_lock, 2000))
        {
            try
            {
                mediaItems = _topItems.OfType<MediaCardItem>().Where(m => m.Active).ToList();
                superItems = _topItems.OfType<SuperIslandItem>().Where(s => s.Active).ToList();

                // 移除超时条目
                for (int i = mediaItems.Count - 1; i >= 0; i--)
                {
                    var elapsed = (now - mediaItems[i].LastUpdateTime) / freq;
                    if (elapsed > MediaCardItem.TimeoutSeconds)
                    {
                        mediaItems[i].Active = false;
                        mediaItems[i].Dispose();
                        _topItems.Remove(mediaItems[i]);
                        mediaItems.RemoveAt(i);
                    }
                }
                for (int i = superItems.Count - 1; i >= 0; i--)
                {
                    var elapsed = (now - superItems[i].LastUpdateTime) / freq;
                    // 对齐 Android：媒体条目 20s、普通条目 12s 自动移除
                    double timeout = superItems[i].State.IsMedia
                        ? SuperIslandItem.MediaTimeoutSeconds
                        : SuperIslandItem.TimeoutSeconds;
                    if (elapsed > timeout)
                    {
                        superItems[i].Active = false;
                        superItems[i].Dispose();
                        _topItems.Remove(superItems[i]);
                        superItems.RemoveAt(i);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }
        else
        {
            // 锁被异常持有：跳过本帧顶部卡片渲染
            overlay.TopOffset = TopCardsOffsetY;
            return;
        }

        var rt = overlay.RenderTarget;
        if (rt == null) return;

        // 自动收起判定（媒体 5s / 超级岛 3s，summaryOnly 禁止展开）
        foreach (var media in mediaItems)
        {
            if (media.IsExpanded && (now - media.ExpandedSince) / freq > MediaCardItem.AutoCollapseSeconds)
                media.IsExpanded = false;
            EnsureMediaResources(media, rt);
        }
        foreach (var si in superItems)
        {
            if (si.IsExpanded && !si.State.SummaryOnly
                && (now - si.ExpandedSince) / freq > SuperIslandItem.AutoCollapseSeconds)
            {
                si.IsExpanded = false;
            }
            EnsureSuperIslandResources(si, rt);
        }

        var ui = GetUiRoot(overlay);

        // 声明式组合：媒体卡片 + 超级岛卡片，纵向零间距排布
        var composer = ui.BeginTopCards();
        UI.Column? topCardsColumn = null;
        composer.Node<Align>("topCards", a =>
        {
            a.XPct = 50f;
            a.YPct = 0f;                 // 锚点取屏幕顶部（配合 OffsetY = 10 即现状的 y 起点）
            a.AnchorAtCenterX = true;
            a.AnchorAtCenterY = false;   // 顶部对齐：卡片从锚点向下排布
            a.OffsetY = TopCardsOffsetY;
            a.ClampToBounds = false;
        }, () =>
        {
            topCardsColumn = composer.Node<UI.Column>("cards", col =>
            {
                col.Spacing = 0f;
                // 每张卡片在屏幕宽度内水平居中（等价现状各自 (screenW - pillW)/2），
                // 纵向由本列按实测高度依次累加（等价现状的 y 递增）
                col.CrossAlignment = CrossAlignment.Center;
                col.FillAvailableWidth = true;
            }, () =>
            {
                foreach (var media in mediaItems)
                    MediaCard.Compose(composer, media, now, freq);

                foreach (var si in superItems)
                    SuperIslandCard.Compose(composer, si, overlay, now, freq);
            });
        });

        ui.EndTopCards(overlay);

        // measure 后的实测总高度写回 TopOffset（等价旧实现 99 行的 overlay.TopOffset = y），
        // 继续作为弹幕轨道起点
        overlay.TopOffset = TopCardsOffsetY
            + (topCardsColumn?.MeasuredSize.Height ?? 0f);
    }
}
