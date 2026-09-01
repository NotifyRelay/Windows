using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    // 按键状态服务（可选注入）
    private IKeyboardStateProvider? _keyboardStateProvider;

    // 按键显示相关常量
    private const float KeyBoxSize = 32;
    private const float KeyBoxPadding = 4;
    private const float KeyBoxMargin = 2;
    private const float KeyBoxRadius = 6;
    private const float KeyFontSize = 14;
    private const float KeyStartX = 20;
    private const float KeyStartY = 20;
    private const float KeyMaxPerRow = 10;

    // 需要显示的按键列表（常用游戏按键）
    private static readonly int[] TrackedKeys = new[]
    {
        0x10, // Shift
        0x11, // Ctrl
        0x12, // Alt
        0x57, // W
        0x41, // A
        0x53, // S
        0x44, // D
        0x20, // Space
        0x45, // E
        0x51, // Q
        0x52, // R
        0x46, // F
        0x43, // C
        0x5A, // Z
        0x58, // X
        0x47, // G
    };

    // 按键显示名称映射
    private static readonly Dictionary<int, string> KeyNames = new()
    {
        [0x10] = "Shift",
        [0x11] = "Ctrl",
        [0x12] = "Alt",
        [0x14] = "Caps",
        [0x20] = "Space",
        [0x25] = "←",
        [0x26] = "↑",
        [0x27] = "→",
        [0x28] = "↓",
    };

    /// <summary>设置键盘状态查询服务（由 DI 注入后调用）。</summary>
    public void SetKeyboardStateProvider(IKeyboardStateProvider? provider)
    {
        // 退订旧 Provider、订阅新 Provider 走共用模板（与罗技电池等元素一致）
        OverlayElementCore.ReplaceProvider(ref _keyboardStateProvider, provider,
            p => p.MappingTriggered += OnMappingTriggered,
            p => p.MappingTriggered -= OnMappingTriggered);
    }

    /// <summary>快捷键映射触发回调：记录提示文本与时间戳（钩子线程调用）。</summary>
    private void OnMappingTriggered(object? sender, KeyMappingDisplayEventArgs e)
    {
        if (string.IsNullOrEmpty(e.DisplayText)) return;
        lock (_lock)
        {
            _keyMappingHintText = e.DisplayText;
            _keyMappingHintTick = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>在左上角渲染键盘按键状态指示器。</summary>
    private void RenderKeyboardState(ScreenOverlay overlay, double now, double freq)
    {
        if (_keyboardStateProvider == null || !_settings.KeyboardOverlayEnabled)
            return;

        var rt = overlay.RenderTarget;
        if (rt == null) return;

        var pressedKeys = _keyboardStateProvider.GetPressedKeys().ToList();

        // 过滤出需要显示的按键
        var displayKeys = pressedKeys
            .Where(k => TrackedKeys.Contains(k) || KeyNames.ContainsKey(k))
            .Distinct()
            .ToList();

        // 映射触发提示（独立于按键状态，无按键按下时仍可能显示）
        var hint = GetKeyMappingHintForRender(out float hintOpacity);
        if (displayKeys.Count == 0 && hint == null) return;

        float x = KeyStartX;
        float y = KeyStartY;
        float opacity = 0.85f;

        using var bgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.6f * opacity));
        using var textBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity));
        using var activeBgBrush = rt.CreateSolidColorBrush(new Color4(0.3f, 0.7f, 1.0f, 0.8f));
        using var format = CreateTextFormat("Segoe UI", DWriteFontWeight.Bold, KeyFontSize);

        foreach (var key in displayKeys)
        {
            string displayText = GetKeyDisplayName(key);

            // 测量文本宽度
            using var layout = _dwFactory.CreateTextLayout(displayText, format, KeyBoxSize * 3, KeyBoxSize);
            float textWidth = layout.Metrics.Width;
            float boxWidth = Math.Max(textWidth + KeyBoxPadding * 2, KeyBoxSize);
            float boxHeight = KeyBoxSize;

            // 绘制背景
            var rect = new RoundedRectangle(new RectangleF(x, y, boxWidth, boxHeight), KeyBoxRadius, KeyBoxRadius);
            rt.FillRoundedRectangle(ref rect, activeBgBrush);

            // 绘制边框
            using var borderBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, 0.5f * opacity));
            rt.DrawRoundedRectangle(rect, borderBrush, 1.5f);

            // 绘制文本（居中）
            float textX = x + (boxWidth - textWidth) / 2;
            float textY = y + (boxHeight - KeyFontSize) / 2;
            rt.DrawTextLayout(new Vector2(textX, textY), layout, textBrush);

            x += boxWidth + KeyBoxMargin;

            // 每行最多 KeyMaxPerRow 个按键
            if ((x - KeyStartX) / (KeyBoxSize + KeyBoxMargin) >= KeyMaxPerRow)
            {
                x = KeyStartX;
                y += KeyBoxSize + KeyBoxMargin;
            }
        }

        // 渲染映射触发提示文本（按键状态下方一行，带超时淡出）
        if (hint != null)
        {
            float hintY = y + KeyBoxSize + KeyBoxMargin;
            using var hintLayout = _dwFactory.CreateTextLayout(hint, format, 600, KeyBoxSize * 2);
            float hintWidth = hintLayout.Metrics.Width;
            float hintBoxWidth = hintWidth + KeyBoxPadding * 2;
            float hintBoxHeight = KeyBoxSize;
            var hintRect = new RoundedRectangle(new RectangleF(KeyStartX, hintY, hintBoxWidth, hintBoxHeight), KeyBoxRadius, KeyBoxRadius);
            using var hintBgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.6f * hintOpacity));
            rt.FillRoundedRectangle(ref hintRect, hintBgBrush);
            using var hintBorderBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, 0.5f * hintOpacity));
            rt.DrawRoundedRectangle(hintRect, hintBorderBrush, 1.5f);
            using var hintTextBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, hintOpacity));
            float hintTextX = KeyStartX + KeyBoxPadding;
            float hintTextY = hintY + (hintBoxHeight - KeyFontSize) / 2;
            rt.DrawTextLayout(new Vector2(hintTextX, hintTextY), hintLayout, hintTextBrush);
        }
    }

    private static string GetKeyDisplayName(int vkCode)
    {
        if (KeyNames.TryGetValue(vkCode, out var name))
            return name;

        // 字母键
        if (vkCode >= 0x41 && vkCode <= 0x5A)
            return ((char)vkCode).ToString();

        // 数字键
        if (vkCode >= 0x30 && vkCode <= 0x39)
            return ((char)vkCode).ToString();

        // F键
        if (vkCode >= 0x70 && vkCode <= 0x87)
            return $"F{vkCode - 0x6F}";

        return $"0x{vkCode:X2}";
    }
}
