using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// OverlayRenderService 的 DeepSeek 余额叠加层渲染 partial。
/// 渲染内容：圆角小卡片 = 余额图标（Segoe MDL2 Assets） + 余额文本（¥xx.xx） + 相对上次的变化量（绿涨/红跌）。
/// 与罗技电池卡片保持同一视觉语言（半透明黑底 + 细边框），仅数据源不同。
/// 数据由主项目 DeepSeekBalanceOverlayFeature 在余额更新事件中推入，本 partial 不直接依赖 Worker 服务。
/// </summary>
public partial class OverlayRenderService
{
    // 余额叠加层状态（_lock 保护）
    private bool _dsbEnabled;
    private string _dsbTargetScreen = "PRIMARY";
    private float _dsbXPct = 20f;
    private float _dsbYPct = 50f;   // 与罗技电池默认位置(20,70)、时间浮窗(50,10)错开，避免默认重叠
    private float _dsbScale = 1f;
    private double? _dsbBalance;      // null = 尚无数据
    private double _dsbChange;        // 相对上一条记录的变化量
    private long _dsbDataVersion;     // 数据版本，变化时触发重建

    // 渲染资源缓存（仅渲染线程访问）：仅在文本 / 数据版本 / 缩放 / 渲染目标变化时重建
    private IDWriteTextLayout? _dsbIconLayout;
    private IDWriteTextLayout? _dsbValueLayout;
    private IDWriteTextLayout? _dsbChangeLayout;
    private ID2D1SolidColorBrush? _dsbBgBrush;
    private ID2D1SolidColorBrush? _dsbBorderBrush;
    private ID2D1SolidColorBrush? _dsbIconBrush;
    private ID2D1SolidColorBrush? _dsbValueBrush;
    private ID2D1SolidColorBrush? _dsbChangeUpBrush;
    private ID2D1SolidColorBrush? _dsbChangeDownBrush;
    private ID2D1DCRenderTarget? _dsbBrushRt;   // 画刷归属的渲染目标
    private long _dsbCacheVersion = -1;
    private float _dsbCacheScale = -1f;

    // 渲染尺寸常量（最终乘以 Scale）
    private const float DsbCardPaddingX = 12f;
    private const float DsbCardPaddingY = 8f;
    private const float DsbCardCornerRadius = 7f;
    private const float DsbIconSize = 20f;      // Segoe MDL2 Assets 图标字号
    private const float DsbValueSize = 14f;     // 余额字号
    private const float DsbChangeSize = 12f;    // 变化量字号
    private const float DsbIconTextGap = 8f;    // 图标与余额间距
    private const float DsbValueChangeGap = 8f; // 余额与变化量间距
    private const float DsbCardMaxWidthFactor = 0.35f; // 单卡片最大宽度 = 屏幕宽度 × 此系数

    /// <summary>余额图标字形（与顶部导航「DeepSeek余额」同一字形，保持视觉一致）。</summary>
    private const string DsbIconGlyph = "\uE8BC";

    /// <summary>更新 DeepSeek 余额叠加层配置（启用、目标屏、位置百分比、缩放）。</summary>
    public void SetDeepSeekBalanceConfig(bool enabled, string targetScreen, float xPct, float yPct, float scale)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过 DeepSeek 余额配置更新");
            return;
        }
        try
        {
            _dsbEnabled = enabled;
            _dsbTargetScreen = string.IsNullOrEmpty(targetScreen) ? "PRIMARY" : targetScreen;
            _dsbXPct = Math.Clamp(xPct, 0f, 100f);
            _dsbYPct = Math.Clamp(yPct, 0f, 100f);
            _dsbScale = OverlayElementCore.ResolveScale(scale, 0.5f, 4f);
            _dsbDataVersion++;
            _displayDirty = true;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>
    /// 推送最新余额与相对上一条记录的变化量（余额服务线程调用）。
    /// balance 传 null 表示暂无数据（如未配置 Token / 查询失败且无历史）。
    /// </summary>
    public void UpdateDeepSeekBalance(double? balance, double change)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            // 渲染线程异常持锁超时：丢弃本次数据，避免业务线程无限阻塞
            return;
        }
        try
        {
            _dsbBalance = balance;
            _dsbChange = change;
            _dsbDataVersion++;
            _displayDirty = true;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>从已保存设置初始化余额叠加层配置（Start 时调用）。</summary>
    private void LoadInitialDeepSeekBalanceConfig()
    {
        var s = _settings;
        SetDeepSeekBalanceConfig(
            s.EnableDeepSeekBalanceMonitor,
            s.DeepSeekBalanceTargetScreen,
            s.DeepSeekBalanceXPercent,
            s.DeepSeekBalanceYPercent,
            s.DeepSeekBalanceScale);
    }

    /// <summary>DeepSeek 余额叠加层是否需要保持渲染（启用即显示）。</summary>
    private bool DeepSeekBalanceActive()
    {
        if (Monitor.TryEnter(_lock, 2000))
        {
            try { return _dsbEnabled; }
            finally { Monitor.Exit(_lock); }
        }
        return false;   // 锁被异常持有：跳过本帧判定
    }

    /// <summary>判断指定覆盖层窗口是否为 DeepSeek 余额显示目标屏（匹配不到目标屏时回退主屏）。</summary>
    private bool IsDeepSeekBalanceTarget(ScreenOverlay o)
    {
        if (o.IsSpan) return false;   // 余额卡片不支持跨屏窗口
        string target;
        if (!Monitor.TryEnter(_lock, 2000))
        {
            return false;   // 锁被异常持有：本帧不绘制余额卡片
        }
        try
        {
            if (!_dsbEnabled) return false;
            target = _dsbTargetScreen;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
        // 目标屏解析复用共用核心（primary / 设备名精确匹配 / 回退主屏），与其他元素共用同一真源
        return OverlayElementCore.IsTargetScreen(o, target,
            _windowManager.Overlays, _windowManager.SpanOverlay, allowSpan: false);
    }

    /// <summary>
    /// 绘制余额卡片：圆角小卡片 = 图标 + 余额 + 变化量。
    /// 尚无数据时余额位置显示占位「--」，保证开关打开即有视觉反馈。
    /// </summary>
    private void RenderDeepSeekBalance(ScreenOverlay overlay)
    {
        if (!IsDeepSeekBalanceTarget(overlay)) return;
        var rt = overlay.RenderTarget;
        if (rt == null) return;

        float scale, xPct, yPct;
        double? balance;
        double change;
        long dataVersion;
        lock (_lock)
        {
            scale = _dsbScale;
            xPct = _dsbXPct;
            yPct = _dsbYPct;
            balance = _dsbBalance;
            change = _dsbChange;
            dataVersion = _dsbDataVersion;
        }

        float iconSize = DsbIconSize * scale;
        float valueSize = DsbValueSize * scale;
        float changeSize = DsbChangeSize * scale;
        float px = DsbCardPaddingX * scale;
        float py = DsbCardPaddingY * scale;
        float radius = DsbCardCornerRadius * scale;
        float gap = DsbIconTextGap * scale;
        float gapVC = DsbValueChangeGap * scale;

        // 文本：余额固定两位小数；变化量仅在存在有效变化时显示
        string valueText = balance.HasValue
            ? "¥" + balance.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            : "¥--";
        bool hasChange = balance.HasValue && Math.Abs(change) >= 0.005;
        string changeText = hasChange ? (change > 0 ? "+" : "-") + Math.Abs(change).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

        EnsureDsbRenderCache(rt, valueText, changeText, hasChange, scale, dataVersion, iconSize, valueSize, changeSize);

        float valueW = _dsbValueLayout?.Metrics.WidthIncludingTrailingWhitespace ?? 0;
        float changeW = hasChange ? (_dsbChangeLayout?.Metrics.WidthIncludingTrailingWhitespace ?? 0) : 0;

        float naturalWidth = px * 2 + iconSize + gap + valueW + (hasChange ? gapVC + changeW : 0);
        float cardMaxWidth = Math.Clamp(overlay.Width * DsbCardMaxWidthFactor, 120f * scale, 540f * scale);
        float cardWidth = MathF.Min(naturalWidth, cardMaxWidth);
        float rowHeight = Math.Max(iconSize, valueSize) + py * 2;

        var (baseX, baseY) = OverlayElementCore.ResolveAnchor(overlay, xPct, yPct);
        float drawX = MathF.Min(baseX, MathF.Max(0, overlay.Width - cardWidth));

        var rect = new RoundedRectangle(new RectangleF(drawX, baseY, cardWidth, rowHeight), radius, radius);
        rt.FillRoundedRectangle(ref rect, _dsbBgBrush!);
        rt.DrawRoundedRectangle(rect, _dsbBorderBrush!, 1f * scale);

        float innerY = baseY + py;
        float contentTopOffset = Math.Max(0f, (rowHeight - py * 2 - iconSize) / 2);

        // 1. 余额图标
        if (_dsbIconLayout != null && _dsbIconBrush != null)
            rt.DrawTextLayout(new Vector2(drawX + px, innerY + contentTopOffset), _dsbIconLayout, _dsbIconBrush);

        // 2. 余额文本（垂直居中）
        float textX = drawX + px + iconSize + gap;
        float valueY = innerY + Math.Max(0, (rowHeight - py * 2 - valueSize) / 2);
        if (_dsbValueLayout != null && _dsbValueBrush != null)
            rt.DrawTextLayout(new Vector2(textX, valueY), _dsbValueLayout, _dsbValueBrush);

        // 3. 变化量（涨绿跌红，垂直居中）
        if (hasChange && _dsbChangeLayout != null)
        {
            float changeY = innerY + Math.Max(0, (rowHeight - py * 2 - changeSize) / 2);
            var brush = change > 0 ? _dsbChangeUpBrush : _dsbChangeDownBrush;
            if (brush != null)
                rt.DrawTextLayout(new Vector2(textX + valueW + gapVC, changeY), _dsbChangeLayout, brush);
        }
    }

    /// <summary>
    /// 确保余额渲染缓存与当前文本 / 数据版本 / 缩放 / 渲染目标一致。
    /// 仅在对应值变化时重建资源，避免每帧创建 DirectWrite 对象与画刷。
    /// </summary>
    private void EnsureDsbRenderCache(ID2D1DCRenderTarget rt, string valueText, string changeText,
        bool hasChange, float scale, long dataVersion, float iconSize, float valueSize, float changeSize)
    {
        bool rtChanged = !ReferenceEquals(_dsbBrushRt, rt);
        bool scaleChanged = !Approximately(_dsbCacheScale, scale);
        // 数据版本在每次余额/配置更新时自增，故版本比对已覆盖余额与变化量的变化
        bool textChanged = _dsbCacheVersion != dataVersion;

        // 画刷与渲染目标绑定：rt 变化时重建
        if (rtChanged || _dsbBgBrush == null)
        {
            _dsbBgBrush?.Dispose();
            _dsbBorderBrush?.Dispose();
            _dsbIconBrush?.Dispose();
            _dsbValueBrush?.Dispose();
            _dsbChangeUpBrush?.Dispose();
            _dsbChangeDownBrush?.Dispose();
            const float opacity = 0.9f;
            _dsbBgBrush = CreateSolidColorBrush(rt, new Color4(0, 0, 0, 0.6f * opacity));
            _dsbBorderBrush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, 0.35f * opacity));
            _dsbIconBrush = CreateSolidColorBrush(rt, new Color4(0.25f, 0.77f, 1.0f, opacity));   // 主题蓝
            _dsbValueBrush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
            _dsbChangeUpBrush = CreateSolidColorBrush(rt, new Color4(0.35f, 0.85f, 0.45f, opacity)); // 涨=绿
            _dsbChangeDownBrush = CreateSolidColorBrush(rt, new Color4(0.95f, 0.42f, 0.42f, opacity)); // 跌=红
            _dsbBrushRt = rt;
        }

        // 文本布局：文本 / 缩放变化时重建（与渲染目标无关）
        if (textChanged || scaleChanged || _dsbValueLayout == null)
        {
            _dsbIconLayout?.Dispose();
            _dsbValueLayout?.Dispose();
            _dsbChangeLayout?.Dispose();

            _dsbIconLayout = _dwFactory.CreateTextLayout(DsbIconGlyph,
                CreateTextFormat("Segoe MDL2 Assets", DWriteFontWeight.Regular, iconSize), iconSize * 2, iconSize * 2);
            _dsbValueLayout = _dwFactory.CreateTextLayout(valueText,
                CreateTextFormat("Microsoft YaHei", DWriteFontWeight.SemiBold, valueSize), 400f * scale, valueSize * 1.4f);
            _dsbValueLayout.WordWrapping = WordWrapping.NoWrap;

            if (hasChange)
            {
                _dsbChangeLayout = _dwFactory.CreateTextLayout(changeText,
                    CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, changeSize), 200f * scale, changeSize * 1.4f);
                _dsbChangeLayout.WordWrapping = WordWrapping.NoWrap;
            }
            else
            {
                _dsbChangeLayout = null;
            }

            _dsbCacheVersion = dataVersion;
            _dsbCacheScale = scale;
        }
    }

    /// <summary>释放 DeepSeek 余额渲染缓存（窗口清理/停止时调用）。</summary>
    private void DisposeDeepSeekBalanceResources()
    {
        try { _dsbIconLayout?.Dispose(); } catch { }
        try { _dsbValueLayout?.Dispose(); } catch { }
        try { _dsbChangeLayout?.Dispose(); } catch { }
        try { _dsbBgBrush?.Dispose(); } catch { }
        try { _dsbBorderBrush?.Dispose(); } catch { }
        try { _dsbIconBrush?.Dispose(); } catch { }
        try { _dsbValueBrush?.Dispose(); } catch { }
        try { _dsbChangeUpBrush?.Dispose(); } catch { }
        try { _dsbChangeDownBrush?.Dispose(); } catch { }
        _dsbIconLayout = _dsbValueLayout = _dsbChangeLayout = null;
        _dsbBgBrush = _dsbBorderBrush = _dsbIconBrush = _dsbValueBrush = null;
        _dsbChangeUpBrush = _dsbChangeDownBrush = null;
        _dsbBrushRt = null;
        _dsbCacheVersion = -1;
        _dsbCacheScale = -1f;
    }
}
