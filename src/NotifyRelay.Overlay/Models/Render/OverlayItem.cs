using System.Collections.Concurrent;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace NotifyRelay.Models.Render;

public enum DanmakuType
{
    Notification,
    Media,
    SuperIsland
}

public abstract class OverlayItem : IDisposable
{
    public DanmakuType Type { get; protected set; }
    public double StartTime { get; set; }
    public bool Active { get; set; } = true;

    public abstract void Render(ID2D1DCRenderTarget rt);
    public abstract void Dispose();
}

public class DanmakuItem : OverlayItem
{
    public string Text { get; set; }
    public byte[]? IconPng { get; set; }
    public DanmakuStyleSettings Settings { get; set; }

    public IDWriteTextLayout? TextLayout { get; set; }
    public ID2D1Bitmap? IconBitmap { get; set; }

    public double SpawnX { get; set; }
    public int TrackIndex { get; set; }
    public float TrackY { get; set; }
    public float TextWidth { get; set; }
    public float TextHeight { get; set; }
    public float TotalWidth { get; set; }

    public string? AppName { get; set; }
    public string DeviceName { get; set; } = string.Empty;

    public DanmakuItem()
    {
        Type = DanmakuType.Notification;
        Text = string.Empty;
        Settings = new DanmakuStyleSettings();
    }

    public override void Render(ID2D1DCRenderTarget rt)
    {
    }

    public override void Dispose()
    {
        TextLayout?.Dispose();
        IconBitmap?.Dispose();
    }
}

public class MediaCardItem : OverlayItem
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public byte[]? CoverPng { get; set; }
    public bool IsPlaying { get; set; }

    public ID2D1Bitmap? CoverBitmap { get; set; }
    public IDWriteTextLayout? TitleLayout { get; set; }
    public IDWriteTextLayout? ArtistLayout { get; set; }

    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;

    public double Opacity { get; set; } = 1.0;
    public double Position { get; set; }
    public double Duration { get; set; }

    public double LastUpdateTime { get; set; }

    /// <summary>跑马灯计时锚点：标题或播放状态变化时重置，用于过长文本的滚动循环起点</summary>
    public double MarqueeAnchorTime { get; set; }

    public const double TimeoutSeconds = 60;

    // 收起/展开状态
    public bool IsExpanded { get; set; } = true;
    public double ExpandedSince { get; set; }  // Stopwatch 时间戳
    public const double AutoCollapseSeconds = 5.0;

    public MediaCardItem()
    {
        Type = DanmakuType.Media;
    }

    public override void Render(ID2D1DCRenderTarget rt)
    {
    }

    public override void Dispose()
    {
        CoverBitmap?.Dispose();
        CoverBitmap = null;
        TitleLayout?.Dispose();
        TitleLayout = null;
        ArtistLayout?.Dispose();
        ArtistLayout = null;
    }
}

public class SuperIslandItem : OverlayItem
{
    public SuperIslandState State { get; set; } = new();
    public byte[]? IconPng { get; set; }
    public ID2D1Bitmap? IconBitmap { get; set; }
    public IDWriteTextLayout? TitleLayout { get; set; }
    public IDWriteTextLayout? SubtitleLayout { get; set; }
    public IDWriteTextLayout? AdditionalTextLayout { get; set; }
    public IDWriteTextLayout? ExtraLayout { get; set; }

    // 多图位图槽（对应 Android 各模板组件图片）
    public ID2D1Bitmap? AvatarBitmap { get; set; }         // chatInfo 头像
    public ID2D1Bitmap? BigImageLeftBitmap { get; set; }   // highlightInfo 左侧大图
    public ID2D1Bitmap? BigImageRightBitmap { get; set; }  // highlightInfo 右侧大图
    public ID2D1Bitmap? PicInfoBitmap { get; set; }        // picInfo 图片
    public ID2D1Bitmap? LeftIconBitmap { get; set; }       // A 区图标
    public ID2D1Bitmap? RightIconBitmap { get; set; }      // B 区图标

    // multiProgressInfo 节点与指针图（对齐 Android MultiProgressCompose）
    public ID2D1Bitmap? MultiForwardBitmap { get; set; }       // picForward 指针
    public ID2D1Bitmap? MultiForwardBoxBitmap { get; set; }    // picForwardBox 指针备选/节点
    public ID2D1Bitmap? MultiMiddleBitmap { get; set; }        // picMiddle 完成节点
    public ID2D1Bitmap? MultiMiddleUnselBitmap { get; set; }   // picMiddleUnselected 未完成节点
    public ID2D1Bitmap? MultiEndBitmap { get; set; }           // picEnd 末端完成
    public ID2D1Bitmap? MultiEndUnselBitmap { get; set; }      // picEndUnselected 末端未完成

    /// <summary>加载失败的图片键集合（Pics 更新时清除），避免每帧重复解析无效图。</summary>
    public HashSet<string> FailedPicKeys { get; } = [];

    /// <summary>网络 URL 图片异步下载缓存（key → PNG 字节），渲染线程读取。</summary>
    public Dictionary<string, byte[]> UrlPngCache { get; } = new();

    /// <summary>正在异步下载的 URL 图片键（去重，防止每帧重复发起请求）。</summary>
    public ConcurrentDictionary<string, byte> UrlFetching { get; } = new();

    public string SourceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;

    public double Opacity { get; set; } = 1.0;
    public double LastUpdateTime { get; set; }
    public const double TimeoutSeconds = 12;

    // 展开/收起状态（对齐 Android：3s 自动收起；媒体条目 20s 移除）
    public bool IsExpanded { get; set; } = true;
    public double ExpandedSince { get; set; }
    public const double AutoCollapseSeconds = 3.0;
    public const double MediaTimeoutSeconds = 20;

    // 收起态文本滚动锚点（对齐 Android AutoScrollText：显示文本变化时重置，长文本循环滚动）
    public string CollapsedScrollKey { get; set; } = string.Empty;
    public double CollapsedScrollTime { get; set; }

    public SuperIslandItem()
    {
        Type = DanmakuType.SuperIsland;
    }

    public override void Render(ID2D1DCRenderTarget rt)
    {
    }

    public override void Dispose()
    {
        IconBitmap?.Dispose();
        TitleLayout?.Dispose();
        SubtitleLayout?.Dispose();
        AdditionalTextLayout?.Dispose();
        ExtraLayout?.Dispose();
        AvatarBitmap?.Dispose();
        BigImageLeftBitmap?.Dispose();
        BigImageRightBitmap?.Dispose();
        PicInfoBitmap?.Dispose();
        LeftIconBitmap?.Dispose();
        RightIconBitmap?.Dispose();
        MultiForwardBitmap?.Dispose();
        MultiForwardBoxBitmap?.Dispose();
        MultiMiddleBitmap?.Dispose();
        MultiMiddleUnselBitmap?.Dispose();
        MultiEndBitmap?.Dispose();
        MultiEndUnselBitmap?.Dispose();
    }
}
