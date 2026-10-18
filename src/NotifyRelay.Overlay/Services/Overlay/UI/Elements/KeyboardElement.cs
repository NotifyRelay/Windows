using NotifyRelay.Models.Render;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay.UI.Elements;

/// <summary>
/// 键盘按键元素：左上角按键框（每行最多 10 个自动换行）+ 映射触发提示（超时淡出）。
/// 提示淡出走 <see cref="OpacityBox"/> 的逐帧不透明度提供者，不触发重组。
/// 键盘状态与设置均直读 Provider / Settings（与旧实现一致）。
/// </summary>
internal sealed class KeyboardElement : IOverlayElement
{
    // 按键显示相关常量（与旧实现一致）
    private const float KeyBoxSize = 32;
    private const float KeyBoxPadding = 4;
    private const float KeyBoxMargin = 2;
    private const float KeyBoxRadius = 6;
    private const float KeyFontSize = 14;
    private const float KeyStartX = 20;
    private const float KeyStartY = 20;
    private const int KeyMaxPerRow = 10;
    private const float Opacity = 0.85f;

    private const int HintTimeoutMs = 1500;

    /// <summary>需要显示的按键列表（常用游戏按键）。</summary>
    private static readonly int[] TrackedKeys =
    [
        0x10, 0x11, 0x12, 0x57, 0x41, 0x53, 0x44, 0x20,
        0x45, 0x51, 0x52, 0x46, 0x43, 0x5A, 0x58, 0x47,
    ];

    private static readonly Dictionary<int, string> KeyNames = new()
    {
        [0x10] = "Shift",
        [0x11] = "Ctrl",
        [0x12] = "Alt",
        [0x14] = "Caps",
        [0x20] = "Space",
        [0x25] = "\u2190",
        [0x26] = "\u2191",
        [0x27] = "\u2192",
        [0x28] = "\u2193",
    };

    /// <summary>切换键（锁定键）显示名称：开启 / 关闭。</summary>
    private static readonly Dictionary<int, (string On, string Off)> ToggleKeyNames = new()
    {
        [0x14] = ("大写", "小写"),
        [0x90] = ("NumLk", "NumLk关"),
        [0x91] = ("ScrLk", "ScrLk关"),
    };

    /// <summary>通常处于开启状态的键（如 Num Lock）：仅在关闭时显示提示。</summary>
    private static readonly HashSet<int> ReverseToggleKeys = [0x90];

    private readonly ElementContext _ctx;

    private IKeyboardStateProvider? _provider;

    // 映射提示（_lock 保护）
    private string? _hintText;
    private long _hintTick;

    public KeyboardElement(ElementContext ctx) => _ctx = ctx;

    public string Name => "Keyboard";

    public void LoadSettings(IOverlaySettings settings) { }

    /// <summary>设置键盘状态查询服务（由 DI 注入后调用）。</summary>
    public void SetProvider(IKeyboardStateProvider? provider)
        // 退订旧 Provider、订阅新 Provider 走共用模板（与罗技电池等元素一致）
        => OverlayElementCore.ReplaceProvider(ref _provider, provider,
            p => p.MappingTriggered += OnMappingTriggered,
            p => p.MappingTriggered -= OnMappingTriggered);

    /// <summary>快捷键映射触发回调：记录提示文本与时间戳（钩子线程调用）。</summary>
    private void OnMappingTriggered(object? sender, KeyMappingDisplayEventArgs e)
    {
        if (string.IsNullOrEmpty(e.DisplayText)) return;
        _ctx.WithLock(() =>
        {
            _hintText = e.DisplayText;
            _hintTick = Stopwatch.GetTimestamp();
        }, string.Empty);
    }

    public bool IsActive() => IsKeyboardActive() || HasHint();

    /// <summary>键盘叠加层是否处于活跃状态：普通按键按下，或任一切换键处于需提示的状态。</summary>
    private bool IsKeyboardActive()
    {
        if (!_ctx.Settings.KeyboardOverlayEnabled || _provider == null) return false;
        if (_provider.GetPressedKeys().Any()) return true;
        return ToggleKeyNames.Any(kv =>
        {
            bool toggled = _provider.IsKeyToggled(kv.Key);
            return ReverseToggleKeys.Contains(kv.Key) ? !toggled : toggled;
        });
    }

    /// <summary>是否存在未超时的映射提示。</summary>
    public bool HasHint()
    {
        if (_hintText == null) return false;
        lock (_ctx.StateLock)
        {
            return _hintText != null
                && (Stopwatch.GetTimestamp() - _hintTick) * 1000 / Stopwatch.Frequency < HintTimeoutMs;
        }
    }

    /// <summary>键盘元素只绘制在主屏且有内容要显示（按键或活动提示）。</summary>
    public bool IsTargetScreen(ScreenOverlay o) => o.IsPrimary && IsActive();

    /// <summary>读取提示文本与淡出不透明度；超时则清空。渲染线程调用。</summary>
    private (string? Text, float Opacity) ReadHint()
    {
        if (_hintText == null) return (null, 0f);
        lock (_ctx.StateLock)
        {
            if (_hintText == null) return (null, 0f);
            long elapsedMs = (Stopwatch.GetTimestamp() - _hintTick) * 1000 / Stopwatch.Frequency;
            if (elapsedMs >= HintTimeoutMs)
            {
                _hintText = null;
                return (null, 0f);
            }
            // 最后 30% 时长淡出
            float remaining = 1f - (float)elapsedMs / HintTimeoutMs;
            float opacity = remaining < 0.3f ? remaining / 0.3f : 1f;
            return (_hintText, opacity);
        }
    }

    /// <summary>当前需要显示的按键文本（切换键按开关状态常显）。</summary>
    private List<string> SnapshotKeys()
    {
        var keys = new List<string>();
        if (!_ctx.Settings.KeyboardOverlayEnabled || _provider == null) return keys;

        foreach (var k in _provider.GetPressedKeys())
        {
            if ((!TrackedKeys.Contains(k) && !KeyNames.ContainsKey(k)) || ToggleKeyNames.ContainsKey(k))
                continue;
            string name = GetKeyDisplayName(k);
            if (!keys.Contains(name)) keys.Add(name);
        }

        // 切换键：常态开启的键（如 Num Lock）仅在关闭时显示，其余键在开启时显示
        foreach (var kv in ToggleKeyNames)
        {
            bool toggled = _provider.IsKeyToggled(kv.Key);
            bool show = ReverseToggleKeys.Contains(kv.Key) ? !toggled : toggled;
            if (!show) continue;
            // 反转键显示「关闭」文案（如 NumLk关），其余显示「开启」文案
            keys.Add(ReverseToggleKeys.Contains(kv.Key) ? kv.Value.Off : kv.Value.On);
        }
        return keys;
    }

    public void Compose(OverlayComposer composer, ScreenOverlay o)
    {
        if (!o.IsPrimary) return;

        var keys = SnapshotKeys();
        var (hint, _) = ReadHint();
        if (keys.Count == 0 && hint == null) return;

        composer.Node<Align>(null, a =>
        {
            // 左上角锚点：XPct/YPct 必须归零，否则默认 50 会把按键放到屏幕中心
            a.XPct = 0f;
            a.YPct = 0f;
            a.AnchorAtCenterX = false;
            a.AnchorAtCenterY = false;
            a.OffsetX = KeyStartX;
            a.OffsetY = KeyStartY;
            a.ClampToBounds = false;
        }, () =>
        {
            // 提示行与按键行的纵向间距 = KeyBoxMargin（对齐现状 hintY = y + KeyBoxSize + KeyBoxMargin）
            composer.Node<Column>(null, col => { col.Spacing = KeyBoxMargin; }, () =>
            {
                if (keys.Count > 0)
                {
                    composer.Node<WrapRow>("keys", w =>
                    {
                        w.Gap = KeyBoxMargin;
                        w.RowGap = KeyBoxMargin;
                        // 复刻现状判据 (nextX - KeyStartX) / (KeyBoxSize + KeyBoxMargin) >= KeyMaxPerRow：
                        // 按「下一个格的左边缘」比较格位序号，宽键不提前换行
                        w.MaxWidth = KeyMaxPerRow * (KeyBoxSize + KeyBoxMargin);
                        w.WrapOnNextLeftOffset = true;
                    }, () =>
                    {
                        for (int i = 0; i < keys.Count; i++)
                        {
                            string keyText = keys[i];
                            int captured = i;
                            composer.Node<Surface>("key" + captured, key =>
                            {
                                key.Radius = KeyBoxRadius;
                                key.Filled = true;
                                key.Fill = new Color4(0.3f, 0.7f, 1f, 0.8f);
                                key.Bordered = true;
                                key.Border = new Color4(1f, 1f, 1f, 0.5f * Opacity);
                                key.BorderWidth = 1.5f;
                                key.Insets = new Insets(KeyBoxPadding, 0f);
                                // 现状 boxWidth = max(textWidth + 2*pad, KeyBoxSize)、boxHeight = KeyBoxSize
                                key.MinWidth = KeyBoxSize;
                                key.MinHeight = KeyBoxSize;
                                // 文本由 CenterContent 在框内居中（不可用 DWrite 文本对齐：
                                // 布局宽度未受限，居中会把字形推到屏幕中部而非框内）
                                key.CenterContent = true;
                            }, () =>
                            {
                                composer.Leaf<Text>(null, t =>
                                {
                                    t.TextValue = keyText;
                                    t.FontFamily = "Segoe UI";
                                    t.Weight = DWriteFontWeight.Bold;
                                    t.FontSize = KeyFontSize;
                                    t.LineHeight = KeyFontSize * 1.4f;
                                    t.ReportedHeight = KeyFontSize;
                                    t.Color = new Color4(1f, 1f, 1f, Opacity);
                                    t.Ellipsis = false;
                                });
                            });
                        }
                    });
                }

                // 映射触发提示（按键状态下方一行，带超时淡出）
                if (hint != null)
                {
                    composer.Node<OpacityBox>("hint", box =>
                    {
                        box.OpacityProvider = ReadHintOpacity;
                    }, () =>
                    {
                        composer.Node<Surface>(null, surface =>
                        {
                            surface.Radius = KeyBoxRadius;
                            surface.Filled = true;
                            surface.Fill = new Color4(0f, 0f, 0f, 0.6f);
                            surface.Bordered = true;
                            surface.Border = new Color4(1f, 1f, 1f, 0.5f);
                            surface.BorderWidth = 1.5f;
                            surface.Insets = new Insets(KeyBoxPadding, 0f);
                            surface.MinHeight = KeyBoxSize;
                            surface.CenterContent = true;
                        }, () =>
                        {
                            composer.Leaf<Text>(null, t =>
                            {
                                t.TextValue = hint;
                                t.FontFamily = "Segoe UI";
                                t.Weight = DWriteFontWeight.Bold;
                                t.FontSize = KeyFontSize;
                                t.LineHeight = KeyFontSize * 1.4f;
                                t.ReportedHeight = KeyFontSize;
                                t.Color = new Color4(1f, 1f, 1f, 1f);
                                t.Ellipsis = false;
                            });
                        });
                    });
                }
            });
        });
    }

    /// <summary>提示淡出阶段的不透明度（逐帧 Provider，不触发重组）。</summary>
    private float ReadHintOpacity()
    {
        lock (_ctx.StateLock)
        {
            if (_hintText == null) return 0f;
            long elapsedMs = (Stopwatch.GetTimestamp() - _hintTick) * 1000 / Stopwatch.Frequency;
            if (elapsedMs >= HintTimeoutMs) return 0f;
            float remaining = 1f - (float)elapsedMs / HintTimeoutMs;
            return remaining < 0.3f ? remaining / 0.3f : 1f;
        }
    }

    public void Reset() { }

    private static string GetKeyDisplayName(int vkCode)
    {
        if (KeyNames.TryGetValue(vkCode, out var name)) return name;
        if (vkCode is >= 0x41 and <= 0x5A) return ((char)vkCode).ToString();   // 字母键
        if (vkCode is >= 0x30 and <= 0x39) return ((char)vkCode).ToString();   // 数字键
        if (vkCode is >= 0x70 and <= 0x87) return $"F{vkCode - 0x6F}";         // F 键
        return $"0x{vkCode:X2}";
    }
}
