using System.Globalization;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay.UI.Elements;

/// <summary>
/// DeepSeek 余额元素：圆角小卡片 = 余额图标（Segoe MDL2 Assets） + 余额文本 + 相对上次的变化量（绿涨/红跌）。
/// 与罗技电池卡片保持同一视觉语言（半透明黑底 + 细边框），仅数据源不同。
/// </summary>
internal sealed class DeepSeekBalanceElement : IOverlayElement
{
    private const float CardPaddingX = 12f;
    private const float CardPaddingY = 8f;
    private const float CardCornerRadius = 7f;
    private const float IconSize = 20f;
    private const float ValueSize = 14f;
    private const float ChangeSize = 12f;
    private const float IconTextGap = 8f;
    private const float ValueChangeGap = 8f;
    private const float CardMaxWidthFactor = 0.35f;
    private const float Opacity = 0.9f;

    /// <summary>余额图标字形（与顶部导航「DeepSeek余额」同一字形）。</summary>
    private const string IconGlyph = "\uE8BC";

    private readonly ElementContext _ctx;

    // 状态（_lock 保护）
    private bool _enabled;
    private string _targetScreen = "PRIMARY";
    private float _xPct = 20f;
    private float _yPct = 50f;
    private float _scale = 1f;
    private double? _balance;
    private double _change;

    public DeepSeekBalanceElement(ElementContext ctx) => _ctx = ctx;

    public string Name => "DeepSeekBalance";

    public void LoadSettings(IOverlaySettings s)
        => SetConfig(s.EnableDeepSeekBalanceMonitor, s.DeepSeekBalanceTargetScreen,
            s.DeepSeekBalanceXPercent, s.DeepSeekBalanceYPercent, s.DeepSeekBalanceScale);

    /// <summary>更新余额叠加层配置（启用、目标屏、位置百分比、缩放）。</summary>
    public void SetConfig(bool enabled, string targetScreen, float xPct, float yPct, float scale)
    {
        _ctx.WithLock(() =>
        {
            _enabled = enabled;
            _targetScreen = string.IsNullOrEmpty(targetScreen) ? "PRIMARY" : targetScreen;
            _xPct = Math.Clamp(xPct, 0f, 100f);
            _yPct = Math.Clamp(yPct, 0f, 100f);
            _scale = ElementContext.ResolveScale(scale, 0.5f, 4f);
        }, "覆盖层数据锁获取超时，跳过 DeepSeek 余额配置更新");
    }

    /// <summary>
    /// 推送最新余额与相对上一条记录的变化量（余额服务线程调用）。
    /// balance 传 null 表示暂无数据（如未配置 Token / 查询失败且无历史）。
    /// </summary>
    public void Update(double? balance, double change)
        => _ctx.WithLock(() =>
        {
            _balance = balance;
            _change = change;
        }, string.Empty);

    public bool IsActive() => _ctx.WithLock(() => _enabled, false);

    public bool IsTargetScreen(ScreenOverlay o)
    {
        if (o.IsSpan) return false;   // 余额卡片不支持跨屏窗口
        var target = _ctx.WithLock<string?>(() => _enabled ? _targetScreen : null, null);
        if (target == null) return false;
        return _ctx.IsTargetScreen(o, target, allowSpan: false);
    }

    public void Compose(OverlayComposer composer, ScreenOverlay o)
    {
        float scale, xPct, yPct;
        double? balance;
        double change;
        lock (_ctx.StateLock)
        {
            if (!_enabled) return;
            scale = _scale;
            xPct = _xPct;
            yPct = _yPct;
            balance = _balance;
            change = _change;
        }

        float iconSize = IconSize * scale;
        float valueSize = ValueSize * scale;
        float changeSize = ChangeSize * scale;
        float px = CardPaddingX * scale;
        float py = CardPaddingY * scale;
        float radius = CardCornerRadius * scale;
        float gap = IconTextGap * scale;
        float gapVC = ValueChangeGap * scale;

        // 文本：余额固定两位小数；变化量仅在存在有效变化时显示
        string valueText = balance.HasValue
            ? "¥" + balance.Value.ToString("F2", CultureInfo.InvariantCulture)
            : "¥--";
        bool hasChange = balance.HasValue && Math.Abs(change) >= 0.005;
        string changeText = hasChange
            ? (change > 0 ? "+" : "-") + Math.Abs(change).ToString("F2", CultureInfo.InvariantCulture)
            : string.Empty;

        float cardMaxWidth = Math.Clamp(o.Width * CardMaxWidthFactor, 120f * scale, 540f * scale);
        float rowHeight = Math.Max(iconSize, valueSize) + py * 2;
        var changeColor = change > 0
            ? new Color4(0.35f, 0.85f, 0.45f, Opacity)   // 涨=绿
            : new Color4(0.95f, 0.42f, 0.42f, Opacity);  // 跌=红

        composer.Node<Align>(null, a =>
        {
            a.XPct = xPct;
            a.YPct = yPct;
            a.AnchorAtCenterX = false; a.AnchorAtCenterY = false;   // 左上角锚点：与旧实现一致（卡片从锚点向右下展开）
            // 旧实现 drawX = min(baseX, max(0, screenW - cardWidth))：越界时左移，等价夹取
            a.ClampToBounds = true;
        }, () =>
        {
            composer.Node<Constrained>(null, c =>
            {
                c.MaxWidth = cardMaxWidth;
            }, () =>
            {
                composer.Node<Surface>("card", s =>
                {
                    s.Radius = radius;
                    s.Filled = true;
                    s.Fill = new Color4(0f, 0f, 0f, 0.6f * Opacity);
                    s.Bordered = true;
                    s.Border = new Color4(1f, 1f, 1f, 0.35f * Opacity);
                    s.BorderWidth = 1f * scale;
                    s.Insets = new Insets(px, py);
                }, () =>
                {
                    composer.Node<Row>("content", r =>
                    {
                        r.Gap = gap;
                        r.CrossAlignment = CrossAlignment.Center;
                    }, () =>
                    {
                        // 1. 余额图标（主题蓝）
                        composer.Leaf<Icon>("icon", i =>
                        {
                            i.Glyph = IconGlyph;
                            i.FontFamily = "Segoe MDL2 Assets";
                            i.Weight = DWriteFontWeight.Regular;
                            i.FontSize = iconSize;
                            i.ReportedWidth = iconSize;
                            i.Color = new Color4(0.25f, 0.77f, 1f, Opacity);
                        });

                        // 2. 余额文本
                        composer.Leaf<Text>("value", t =>
                        {
                            t.TextValue = valueText;
                            t.FontFamily = "Microsoft YaHei";
                            t.Weight = DWriteFontWeight.SemiBold;
                            t.FontSize = valueSize;
                            t.LineHeight = valueSize * 1.4f;
                            t.ReportedHeight = Math.Max(iconSize, valueSize);
                            t.Color = new Color4(1f, 1f, 1f, Opacity);
                            t.Ellipsis = false;
                        });

                        // 3. 变化量（涨绿跌红），无有效变化时不声明节点
                        if (hasChange)
                        {
                            composer.Leaf<Text>("change", t =>
                            {
                                t.TextValue = changeText;
                                t.FontFamily = "Microsoft YaHei";
                                t.Weight = DWriteFontWeight.Normal;
                                t.FontSize = changeSize;
                                t.LineHeight = changeSize * 1.4f;
                                t.ReportedHeight = Math.Max(iconSize, valueSize);
                                t.Color = changeColor;
                                t.Ellipsis = false;
                            });
                        }
                    });
                });
            });
        });
    }

    public void Reset() { }
}
