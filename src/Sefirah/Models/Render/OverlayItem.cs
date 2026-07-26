using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Windows.UI;

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
        TitleLayout?.Dispose();
        ArtistLayout?.Dispose();
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

    public string SourceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;

    public double Opacity { get; set; } = 1.0;
    public double LastUpdateTime { get; set; }
    public const double TimeoutSeconds = 10;

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
    }
}
