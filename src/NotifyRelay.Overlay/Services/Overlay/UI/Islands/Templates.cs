using NotifyRelay.Models.Render;
using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteTextAlignment = Vortice.DirectWrite.TextAlignment;

namespace NotifyRelay.Services.Overlay.UI.Islands;

/// <summary>
/// 展开态 12 个模板的声明式实现。
/// 每个模板只描述「UI 长什么样」，高度由布局实测得出（替代旧的 MeasureExpandedTemplate 手工同步公式）。
/// 需要绝对定位的局部（右上角计时器、右侧大图）使用 <see cref="Absolute"/> + <see cref="Stack"/> 保真。
/// </summary>
internal static class Templates
{
    private const float Opacity = 0.9f;

    // ---------- 易：PicInfo ----------

    public static void PicInfo(OverlayComposer c, SuperIslandItem item, PicInfoData pic, float contentWidth)
    {
        const float PicSize = 48f;
        const float TextOffsetY = 14f;
        float textW = contentWidth - PicSize - 12f;

        // 现状高度 = picSize + 4
        c.Node<Column>("picInfoWrap", col => { col.Spacing = 0f; }, () =>
        {
            c.Node<Row>("picInfo", r => { r.Gap = 12f; r.CrossAlignment = CrossAlignment.Start; }, () =>
            {
                BitmapOrPlaceholder(c, item.PicInfoBitmap, pic.Pic != null, PicSize, Opacity);

                if (!string.IsNullOrEmpty(pic.Title))
                {
                    var color = ColorHexParser.ResolveTheme(pic.ColorTitle, pic.ColorTitleDark, new Color4(1f, 1f, 1f, 1f));
                    OffsetText(c, "title", TextOffsetY, t =>
                    {
                        t.TextValue = pic.Title!;
                        t.FontFamily = "Microsoft YaHei";
                        t.Weight = DWriteFontWeight.Bold;
                        t.FontSize = 14f;
                        t.LineHeight = 20f;
                        t.ReportedHeight = 20f;
                        t.MaxWidth = textW;
                        t.Color = new Color4(color.R, color.G, color.B, Opacity);
                    });
                }
            });
            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 4f; });
        });
    }

    // ---------- 易：CoverInfo ----------

    public static void CoverInfo(OverlayComposer c, SuperIslandItem item, CoverInfoData cover, float contentWidth)
    {
        const float CoverSize = 48f;
        float textW = contentWidth - CoverSize - 12f;

        // 现状高度 = max(coverSize, 文本高) + 4
        c.Node<Column>("coverInfoWrap", col => { col.Spacing = 0f; }, () =>
        {
            c.Node<Row>("coverInfo", r => { r.Gap = 12f; r.CrossAlignment = CrossAlignment.Start; }, () =>
            {
                BitmapOrPlaceholder(c, item.CoverInfoBitmap, cover.PicCover != null, CoverSize, Opacity);

                c.Node<Column>("text", col2 => { col2.Spacing = 0f; col2.MaxChildWidth = textW; }, () =>
                {
                    var titleColor = ColorHexParser.ResolveTheme(cover.ColorTitle, cover.ColorTitleDark, new Color4(1f, 1f, 1f, 1f));
                    var contentColor = ColorHexParser.ResolveTheme(cover.ColorContent, cover.ColorContentDark, new Color4(0.8f, 0.8f, 0.8f, 1f));
                    var subColor = ColorHexParser.ResolveTheme(cover.ColorSubContent, cover.ColorSubContentDark, new Color4(0.6f, 0.6f, 0.6f, 1f));

                    Line(c, "title", cover.Title, textW, 15f, DWriteFontWeight.Bold, 22f, titleColor);
                    Line(c, "content", cover.Content, textW, 12f, DWriteFontWeight.Normal, 18f, contentColor);
                    Line(c, "sub", cover.SubContent, textW, 12f, DWriteFontWeight.Normal, 18f, subColor);
                });
            });
            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 4f; });
        });
    }

    // ---------- 易：IconTextInfo ----------

    public static void IconText(OverlayComposer c, SuperIslandItem item, IconTextInfoData info, float contentWidth)
    {
        const float IconSize = 48f;
        const float IconBox = 56f;
        float textW = contentWidth - IconBox - 12f;

        // 现状高度 = max(iconBox, 文本高) + 4
        c.Node<Column>("iconTextWrap", col => { col.Spacing = 0f; }, () =>
        {
            c.Node<Row>("iconText", r => { r.Gap = 12f; r.CrossAlignment = CrossAlignment.Start; }, () =>
            {
                // 左图标：56x56 显示区，48 图垂直居中
                c.Node<Box>("iconBox", b => { b.Width = IconBox; b.Height = IconBox; }, () =>
                {
                    c.Leaf<Bitmap>(null, b =>
                    {
                        b.Source = () => item.IconTextInfoBitmap;
                        b.DrawSize = IconSize;
                        b.Opacity = Opacity;
                        b.PlaceholderWhenEmpty = info.IconKey != null;
                    });
                });

                c.Node<Column>("text", col2 => { col2.Spacing = 0f; col2.MaxChildWidth = textW; }, () =>
                {
                    var titleColor = ColorHexParser.ResolveTheme(info.ColorTitle, info.ColorTitleDark, new Color4(1f, 1f, 1f, 1f));
                    var contentColor = ColorHexParser.ResolveTheme(info.ColorContent, info.ColorContentDark, new Color4(0.8f, 0.8f, 0.8f, 1f));

                    Line(c, "title", info.Title, textW, 15f, DWriteFontWeight.Bold, 22f, titleColor);
                    Line(c, "content", info.Content, textW, 12f, DWriteFontWeight.Normal, 18f, contentColor);
                    Line(c, "sub", info.SubContent, textW, 12f, DWriteFontWeight.Normal, 18f, contentColor, 0.7f * Opacity);
                });
            });
            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 4f; });
        });
    }

    // ---------- 易：AnimText ----------

    public static void AnimText(OverlayComposer c, SuperIslandItem item, AnimTextInfoData anim, float contentWidth)
    {
        const float IconSize = 40f;
        float textW = contentWidth - IconSize - 12f;

        // 现状高度 = max(iconSize, 文本高) + 4
        c.Node<Column>("animWrap", col => { col.Spacing = 0f; }, () =>
        {
            c.Node<Row>("animText", r => { r.Gap = 12f; r.CrossAlignment = CrossAlignment.Start; }, () =>
            {
                BitmapOrPlaceholder(c, item.IconBitmap, anim.IconSrc != null, IconSize, Opacity);

                c.Node<Column>("text", col2 => { col2.Spacing = 0f; col2.MaxChildWidth = textW; }, () =>
                {
                    var titleColor = ColorHexParser.ResolveTheme(anim.ColorTitle, anim.ColorTitleDark, new Color4(1f, 1f, 1f, 1f));
                    var contentColor = ColorHexParser.ResolveTheme(anim.ColorContent, anim.ColorContentDark, new Color4(0.75f, 0.75f, 0.75f, 1f));
                    var timerColor = ColorHexParser.ResolveTheme(anim.ColorTitle, anim.ColorTitleDark, new Color4(0.25f, 0.77f, 1f, 1f));

                    Line(c, "title", anim.Title, textW, 15f, DWriteFontWeight.Bold, 22f, titleColor);
                    if (anim.TimerInfo != null)
                    {
                        Line(c, "timer", SuperIslandCard.FormatDigitTimer(anim.TimerInfo), textW, 15f,
                            DWriteFontWeight.Normal, 22f, timerColor, Opacity, "Consolas");
                    }
                    Line(c, "content", anim.Content, textW, 12f, DWriteFontWeight.Normal, 18f, contentColor);
                });
            });
            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 4f; });
        });
    }

    // ---------- 易/中：ChatInfo ----------

    public static void ChatInfo(OverlayComposer c, SuperIslandItem item, ChatInfoData chat, float contentWidth)
    {
        const float AvatarSize = 48f;
        const float TimerWidth = 70f;
        float textW = contentWidth - AvatarSize - 12f;

        c.Node<Column>("chatInfo", col => { col.Spacing = 0f; col.MaxChildWidth = contentWidth; }, () =>
        {
            c.Node<Constrained>(null, k => { k.FixedWidth = contentWidth; k.FixedHeight = AvatarSize; }, () =>
            {
                c.Node<Stack>("chatStack", static _ => { }, () =>
                {
                    c.Node<Row>("main", r => { r.Gap = 12f; r.CrossAlignment = CrossAlignment.Start; }, () =>
                    {
                        // 圆形头像（无位图时绘制占位圆）
                        c.Node<Box>("avatarBox", b => { b.Width = AvatarSize; b.Height = AvatarSize; }, () =>
                        {
                            c.Node<CircleClip>(null, clip => { clip.Diameter = AvatarSize; }, () =>
                            {
                                c.Leaf<Bitmap>(null, b =>
                                {
                                    b.Source = () => item.AvatarBitmap;
                                    b.DrawSize = AvatarSize;
                                    b.Opacity = Opacity;
                                    b.PlaceholderWhenEmpty = chat.PicProfile != null;
                                });
                            });
                        });

                        c.Node<Constrained>("text", k => { k.FixedWidth = textW; }, () =>
                        {
                            c.Node<Column>(null, col2 => { col2.Spacing = 0f; col2.MaxChildWidth = textW; }, () =>
                            {
                                var titleColor = ColorHexParser.ResolveTheme(chat.ColorTitle, chat.ColorTitleDark, new Color4(1f, 1f, 1f, 1f));
                                var contentColor = ColorHexParser.ResolveTheme(chat.ColorContent, chat.ColorContentDark, new Color4(0.75f, 0.75f, 0.75f, 1f));
                                Line(c, "title", chat.Title, textW, 14f, DWriteFontWeight.Bold, 20f, titleColor);
                                Line(c, "content", chat.Content, textW, 12f, DWriteFontWeight.Normal, 18f, contentColor);
                            });
                        });
                    });

                    // 计时器（右对齐小字）
                    if (chat.TimerInfo != null)
                    {
                        c.Node<Absolute>("timer", a => { a.FromRight = true; a.Left = 0f; a.Top = 0f; }, () =>
                        {
                            c.Node<Constrained>(null, k => { k.FixedWidth = TimerWidth; k.FixedHeight = 16f; }, () =>
                            {
                                c.Leaf<Text>(null, t =>
                                {
                                    t.TextValue = SuperIslandCard.FormatDigitTimer(chat.TimerInfo!);
                                    t.FontFamily = "Consolas";
                                    t.Weight = DWriteFontWeight.Normal;
                                    t.FontSize = 11f;
                                    t.LineHeight = 16f;
                                    t.ReportedHeight = 16f;
                                    t.Ellipsis = false;
                                    t.Color = new Color4(0.7f, 0.7f, 0.7f, Opacity);
                                });
                            });
                        });
                    }
                });
            });

            // 现状返回 max(48, 文本高) + 6：用尾部间隔补足 6px（文本更高时该间隔被 Stack 吸收后仍保留）
            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 6f; });
        });
    }

    // ---------- 中：BaseInfo ----------

    public static void BaseInfo(OverlayComposer c, SuperIslandItem item, BaseInfoData bi, float contentWidth)
    {
        // 文本回退取色：Title/Content 为空而回退到 SubTitle/SubContent 时，改用对应的副文本颜色
        var titleText = bi.Title ?? bi.SubTitle;
        var titleColor = bi.Title != null
            ? ColorHexParser.PreferDark(bi.ColorTitle, bi.ColorTitleDark)
            : ColorHexParser.PreferDark(bi.ColorSubTitle, bi.ColorSubTitleDark);
        var contentText = bi.Content ?? bi.SubContent;
        var contentColorHex = bi.Content != null
            ? ColorHexParser.PreferDark(bi.ColorContent, bi.ColorContentDark)
            : ColorHexParser.PreferDark(bi.ColorSubContent, bi.ColorSubContentDark);
        var extraColorHex = ColorHexParser.PreferDark(bi.ColorExtraTitle, bi.ColorExtraTitleDark);

        c.Node<Column>("baseInfo", col => { col.Spacing = 0f; col.MaxChildWidth = contentWidth; }, () =>
        {
            // type=1：次要文本在上；type=2：主要文本在上
            if (bi.Type == 1)
            {
                HtmlLine(c, "content", contentText, contentWidth, 12f, false, contentColorHex, 18f, 0.75f);
                HtmlLine(c, "title", titleText, contentWidth, 14f, true, titleColor, 20f, 1f);
                HtmlLine(c, "extra", bi.ExtraTitle, contentWidth, 12f, false, extraColorHex, 18f, 0.75f);
            }
            else
            {
                HtmlLine(c, "title", titleText, contentWidth, 14f, true, titleColor, 20f, 1f);
                if (!string.IsNullOrEmpty(bi.ExtraTitle))
                    HtmlLine(c, "extra", bi.ExtraTitle, contentWidth, 14f, true, extraColorHex, 20f, 1f);
                HtmlLine(c, "content", contentText, contentWidth, 12f, false, contentColorHex, 18f, 0.75f);
            }

            // 特殊标签（specialTitle：圆角背景块），与内容之间固定占用 22（18 高 + 4 间距）
            if (!string.IsNullOrEmpty(bi.SpecialTitle))
            {
                var tagBg = ColorHexParser.ResolveTheme(bi.ColorSpecialBg, bi.ColorSpecialBgDark, new Color4(0.3f, 0.3f, 0.3f, 1f));
                var tagText = ColorHexParser.ResolveTheme(bi.ColorSpecialTitle, bi.ColorSpecialTitleDark, new Color4(0.8f, 0.8f, 0.8f, 1f));
                c.Node<Tag>("tag", tag =>
                {
                    tag.Radius = 4f;
                    tag.Background = tagBg;
                    tag.BackgroundOpacity = 0.8f;
                    tag.Insets = new Insets(6f, 1f, 6f, 0f);
                    tag.MinHeight = 18f;
                }, () =>
                {
                    c.Leaf<Text>(null, t =>
                    {
                        t.TextValue = bi.SpecialTitle!;
                        t.FontFamily = "Microsoft YaHei";
                        t.Weight = DWriteFontWeight.Normal;
                        t.FontSize = 11f;
                        t.LineHeight = 16f;
                        t.ReportedHeight = 16f;
                        t.Ellipsis = false;
                        t.Color = new Color4(tagText.R, tagText.G, tagText.B, Opacity);
                    });
                });
                c.Leaf<Spacer>("tagTail", sp => { sp.FixedHeight = 4f; });
            }

            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 2f; });
        });
    }

    // ---------- 中：Actions / Hint ----------

    public static void Actions(OverlayComposer c, SuperIslandItem item, ParamV2 pv, float contentWidth)
    {
        var hint = pv.HintInfo;
        var actions = pv.TextButton?.Actions ?? pv.Actions ?? [];
        int count = Math.Min(actions.Count, 2);

        c.Node<Column>("actions", col => { col.Spacing = 0f; col.MaxChildWidth = contentWidth; }, () =>
        {
            if (hint != null)
            {
                if (!string.IsNullOrEmpty(hint.Title))
                {
                    var color = ColorHexParser.ResolveTheme(hint.ColorTitle, hint.ColorTitleDark, new Color4(1f, 1f, 1f, 1f));
                            Line(c, "hintTitle", hint.Title, contentWidth, 14f, DWriteFontWeight.Bold, 20f, color);
                    // 现状：文本 20 高但累加 22
                    c.Leaf<Spacer>("hintTitleTail", sp => { sp.FixedHeight = 2f; });
                }
                if (!string.IsNullOrEmpty(hint.SubTitle))
                {
                    var color = ColorHexParser.ResolveTheme(hint.ColorSubTitle, hint.ColorSubTitleDark, new Color4(0.8f, 0.8f, 0.8f, 1f));
                    Line(c, "hintSub", hint.SubTitle, contentWidth, 12f, DWriteFontWeight.Normal, 18f, color);
                    // 现状：文本 18 高但累加 20
                    c.Leaf<Spacer>("hintSubTail", sp => { sp.FixedHeight = 2f; });
                }
                if (hint.ActionInfo != null) actions = [hint.ActionInfo];
            }

            count = Math.Min(actions.Count, 2);
            if (count > 0)
            {
                ComposeButtonRow(c, actions, count, contentWidth, radius: 8f);
                c.Leaf<Spacer>("btnTail", sp => { sp.FixedHeight = 4f; });
            }

            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 2f; });
        });
    }

    /// <summary>按钮行：等分宽度 + 8 间距（对齐现状 btnW = (avail - gap*(count-1))/count）。</summary>
    private static void ComposeButtonRow(OverlayComposer c, List<ActionData> actions, int count,
        float contentWidth, float radius)
    {
        const float Gap = 8f;
        c.Node<Row>("buttons", r =>
        {
            r.Gap = Gap;
            r.CrossAlignment = CrossAlignment.Start;
            r.EqualWidth = true;
        }, () =>
        {
            for (int i = 0; i < count; i++)
            {
                var action = actions[i];
                int captured = i;
                c.Node<Button>("btn" + captured, b =>
                {
                    b.Radius = radius;
                    b.Height = 30f;
                    b.Background = ColorHexParser.ResolveTheme(action.ActionBgColor, action.ActionBgColorDark,
                        new Color4(0.22f, 0.22f, 0.22f, 1f));
                    b.BackgroundOpacity = 0.8f;
                    b.TextColor = ColorHexParser.ResolveTheme(action.ActionTitleColor, action.ActionTitleColorDark,
                        new Color4(0.29f, 0.56f, 0.94f, 1f));
                    b.TextValue = action.ActionTitle ?? action.Action ?? "按钮";
                    b.FontSize = 14f;
                    b.Insets = new Insets(6f, 0f);
                }, () => { });
            }
        });
    }

    // ---------- 中：HighlightInfoV3 ----------

    public static void HighlightV3(OverlayComposer c, SuperIslandItem item, HighlightInfoV3Data v3, float contentWidth)
    {
        // 划线开关通过参数传给 Canvas 绘制回调（每个卡片独立的 ShowSecondaryLine）
        var primaryColor = ColorHexParser.ResolveTheme(v3.PrimaryColor, v3.PrimaryColorDark, new Color4(0.25f, 0.77f, 1f, 1f));
        var secondaryColor = ColorHexParser.ResolveTheme(v3.SecondaryColor, v3.SecondaryColorDark, new Color4(0.6f, 0.6f, 0.6f, 1f));
        var tagTextColor = ColorHexParser.ResolveTheme(v3.HighLightTextColor, v3.HighLightTextColorDark, new Color4(1f, 1f, 1f, 1f));
        var tagBgColor = ColorHexParser.ResolveTheme(v3.HighLightBgColor, v3.HighLightBgColorDark, new Color4(0.25f, 0.77f, 1f, 1f));
        bool showSecondaryLine = v3.ShowSecondaryLine;

        c.Node<Column>("v3", col => { col.Spacing = 0f; col.MaxChildWidth = contentWidth; }, () =>
        {
            // 主文本（高亮）20sp
            Line(c, "primary", v3.PrimaryText, contentWidth, 20f, DWriteFontWeight.Bold, 26f, primaryColor);

            // 补充文本（可选划线）
            if (!string.IsNullOrEmpty(v3.SecondaryText))
            {
                c.Leaf<Canvas>("secondary", cv =>
                {
                    cv.FixedSize = new Size(contentWidth, 18f);
                    cv.OnPaint = (s, r) => PaintSecondary(s, r, v3.SecondaryText!, secondaryColor, showSecondaryLine);
                });
            }

            // 文字标签（圆角背景块），现状占用 24 = 20 高 + 4 间距
            if (!string.IsNullOrEmpty(v3.HighLightText))
            {
                c.Node<Tag>("tag", tag =>
                {
                    tag.Radius = 10f;
                    tag.Background = tagBgColor;
                    tag.BackgroundOpacity = Opacity;
                    tag.Insets = new Insets(8f, 2f, 8f, 0f);
                    tag.MinHeight = 20f;
                }, () =>
                {
                    c.Leaf<Text>(null, t =>
                    {
                        t.TextValue = v3.HighLightText!;
                        t.FontFamily = "Microsoft YaHei";
                        t.Weight = DWriteFontWeight.Normal;
                        t.FontSize = 12f;
                        t.LineHeight = 18f;
                        t.ReportedHeight = 18f;
                        t.Ellipsis = false;
                        t.Color = new Color4(tagTextColor.R, tagTextColor.G, tagTextColor.B, Opacity);
                    });
                });
                c.Leaf<Spacer>("tagTail", sp => { sp.FixedHeight = 4f; });
            }

            // 圆头图文按钮（整宽 30 高，现状累加 34）
            var action = v3.ActionInfo;
            if (action != null)
            {
                c.Node<Button>("button", b =>
                {
                    b.Radius = 15f;
                    b.Height = 30f;
                    b.Background = ColorHexParser.ResolveTheme(action.ActionBgColor, action.ActionBgColorDark,
                        new Color4(0.22f, 0.22f, 0.22f, 1f));
                    b.BackgroundOpacity = 0.8f;
                    b.TextColor = ColorHexParser.ResolveTheme(action.ActionTitleColor, action.ActionTitleColorDark,
                        new Color4(0.29f, 0.56f, 0.94f, 1f));
                    b.TextValue = action.ActionTitle ?? action.Action ?? "按钮";
                    b.FontSize = 14f;
                    b.Insets = new Insets(6f, 0f);
                }, () => { });
                c.Leaf<Spacer>("buttonTail", sp => { sp.FixedHeight = 4f; });
            }

            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 2f; });
        });
    }

    private static void PaintSecondary(PaintScope s, Rect r, string text, Color4 color, bool showLine)
    {
        using var format = s.CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 12f);
        using var layout = s.CreateTruncatedLayout(text, format, MathF.Max(1f, r.Width), 18f);
        var brush = s.BrushWithOpacity(new Color4(color.R, color.G, color.B, Opacity));
        s.Rt.DrawTextLayout(new Vector2(r.X, r.Y), layout, brush);
        if (!showLine) return;
        float lineY = r.Y + 9f;
        float lineW = MathF.Min(layout.Metrics.WidthIncludingTrailingWhitespace, r.Width);
        s.Rt.DrawLine(new Vector2(r.X, lineY), new Vector2(r.X + lineW, lineY), brush, 1f);
    }



    // ---------- 中：ParamIsland ----------

    public static void ParamIsland(OverlayComposer c, SuperIslandItem item, ParamIslandData island, float contentWidth)
    {
        const float IconSize = 40f;

        c.Node<Column>("paramIsland", col => { col.Spacing = 0f; col.MaxChildWidth = contentWidth; }, () =>
        {
            // smallIslandArea：摘要文本区（次文本与主文本之间固定 2px 偏移）
            var small = island.SmallIslandArea;
            if (small != null)
            {
                if (!string.IsNullOrEmpty(small.PrimaryText))
                {
                    HtmlLine(c, "smallPrimary", small.PrimaryText, contentWidth, 14f, true,
                        null, 20f, Opacity, new Color4(1f, 1f, 1f, Opacity));
                }
                if (!string.IsNullOrEmpty(small.SecondaryText))
                {
                    c.Leaf<Spacer>("smallGap", sp => { sp.FixedHeight = 2f; });
                    HtmlLine(c, "smallSecondary", small.SecondaryText, contentWidth, 12f, false,
                        null, 18f, Opacity, new Color4(0.55f, 0.55f, 0.55f, Opacity));
                    c.Leaf<Spacer>("smallTail", sp => { sp.FixedHeight = 2f; });
                }
            }

            // bigIslandArea：图标 + 主/次文本行
            var big = island.BigIslandArea;
            if (big == null) return;

            c.Leaf<Spacer>("rowGap", sp => { sp.FixedHeight = 8f; });

            float textW = contentWidth;
            bool hasLeft = !string.IsNullOrEmpty(big.LeftImage);
            bool hasRight = !string.IsNullOrEmpty(big.RightImage);
            if (hasLeft) textW -= IconSize + 12f;
            if (hasRight) textW -= IconSize + 12f;
            if (textW < 0f) textW = 0f;

            c.Node<Row>("bigRow", r => { r.Gap = 12f; r.CrossAlignment = CrossAlignment.Start; }, () =>
            {
                if (hasLeft)
                {
                    BitmapOrPlaceholder(c, item.LeftIconBitmap, true, IconSize, Opacity);
                }

                c.Node<Column>("bigText", col2 => { col2.Spacing = 0f; col2.MaxChildWidth = textW; }, () =>
                {
                    if (!string.IsNullOrEmpty(big.PrimaryText))
                    {
                        HtmlLine(c, "bigPrimary", big.PrimaryText, textW, 14f, true,
                            null, 20f, Opacity, new Color4(1f, 1f, 1f, Opacity));
                    }
                    if (!string.IsNullOrEmpty(big.SecondaryText))
                    {
                        c.Leaf<Spacer>("bigGap", sp => { sp.FixedHeight = 2f; });
                        HtmlLine(c, "bigSecondary", big.SecondaryText, textW, 12f, false,
                            null, 18f, Opacity, new Color4(0.55f, 0.55f, 0.55f, Opacity));
                    }
                });

                if (hasRight)
                {
                    BitmapOrPlaceholder(c, item.RightIconBitmap, true, IconSize, Opacity);
                }
            });
        });
    }

    // ---------- 中：Default ----------

    public static void Default(OverlayComposer c, SuperIslandItem item, float contentWidth)
    {
        var state = item.State;
        const float IconSize = 28f;

        string titleText = state.Title ?? "";
        if (!string.IsNullOrEmpty(state.Subtitle))
            titleText = string.IsNullOrEmpty(titleText) ? state.Subtitle : $"{titleText} · {state.Subtitle}";

        string timerText = state.GetDisplayTime();
        string? progressText = state.GetProgressText();
        string rightText = !string.IsNullOrEmpty(timerText) ? timerText : progressText ?? "";

        c.Node<Stack>("default", static _ => { }, () =>
        {
            float tx = item.IconBitmap != null ? IconSize + 8f : 0f;
            float textW = contentWidth - tx;

            c.Node<Row>("main", r => { r.Gap = 8f; r.CrossAlignment = CrossAlignment.Start; }, () =>
            {
                if (item.IconBitmap != null)
                {
                    c.Leaf<Bitmap>("icon", b =>
                    {
                        b.Source = () => item.IconBitmap;
                        b.DrawSize = IconSize;
                        b.Opacity = Opacity;
                    });
                }

                c.Node<Column>("text", col => { col.Spacing = 0f; col.MaxChildWidth = textW; }, () =>
                {
                    HtmlLine(c, "title", titleText, textW, 14f, true, null, 22f, Opacity,
                        new Color4(1f, 1f, 1f, Opacity));

                    if (!string.IsNullOrEmpty(state.AdditionalText))
                    {
                        HtmlLine(c, "additional", state.AdditionalText, textW, 11f, false, null, 18f, Opacity,
                            new Color4(0.7f, 0.7f, 0.7f, Opacity));
                    }

                    // Extra（展开时第三行）
                    if (state.HasExtra && item.ExtraLayout != null)
                    {
                        c.Leaf<Canvas>("extra", cv =>
                        {
                            cv.FixedSize = new Size(textW, 20f);
                            cv.OnPaint = (s, r) =>
                            {
                                var brush = s.BrushWithOpacity(new Color4(0.6f, 0.8f, 1f, Opacity));
                                s.Rt.DrawTextLayout(new Vector2(r.X, r.Y), item.ExtraLayout!, brush);
                            };
                        });
                    }

                    // 进度条（底部），现状 progY = ty + 6 且累加 12
                    if (state.HasProgress)
                    {
                        c.Leaf<Spacer>("progGap", sp => { sp.FixedHeight = 6f; });
                        c.Leaf<ProgressBar>("progress", p =>
                        {
                            p.Height = 3f;
                            p.Radius = 1.5f;
                            p.TrackColor = new Color4(0.35f, 0.35f, 0.35f, Opacity * 0.6f);
                            p.FillColor = new Color4(0.3f, 0.7f, 1f, Opacity);
                            p.Progress = Math.Clamp(state.Progress, 0, 100);
                            p.Opacity = Opacity;
                        });
                        c.Leaf<Spacer>("progTail", sp => { sp.FixedHeight = 3f; });
                    }

                    c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 2f; });
                });
            });

            // 计时器 / 进度文本（右上角，x = contentWidth - 70）
            if (!string.IsNullOrEmpty(rightText))
            {
                c.Node<Absolute>("right", a => { a.Left = contentWidth - 70f; a.Top = 2f; }, () =>
                {
                    c.Node<Constrained>(null, k => { k.FixedWidth = 70f; k.FixedHeight = 16f; }, () =>
                    {
                        c.Leaf<Text>(null, t =>
                        {
                            t.TextValue = rightText;
                            t.FontFamily = "Consolas";
                            t.Weight = DWriteFontWeight.Normal;
                            t.FontSize = 11f;
                            t.LineHeight = 16f;
                            t.ReportedHeight = 16f;
                            t.Ellipsis = false;
                            t.Color = new Color4(0.7f, 0.7f, 0.7f, Opacity);
                        });
                    });
                });
            }
        });
    }

    // ---------- 难：Highlight ----------

    public static void Highlight(OverlayComposer c, SuperIslandItem item, HighlightInfoData hi, float contentWidth)
    {
        const float IconSize = 40f;
        const float BigImageSize = 44f;
        const float BigImageGap = 6f;

        float effIconSize = hi.IconOnly ? 48f : IconSize;
        bool hasLeft = item.BigImageLeftBitmap != null;
        bool hasRight = item.BigImageRightBitmap != null;
        float bigImages = (hasLeft ? BigImageSize + BigImageGap : 0f) + (hasRight ? BigImageSize : 0f);
        float textW = contentWidth - effIconSize - 12f - bigImages;
        if (textW < 0f) textW = 0f;

        var titleColor = ColorHexParser.ResolveTheme(hi.ColorTitle, hi.ColorTitleDark, new Color4(0.25f, 0.77f, 1f, 1f));
        var contentColor = ColorHexParser.ResolveTheme(hi.ColorContent, hi.ColorContentDark, new Color4(0.8f, 0.8f, 0.8f, 1f));
        var subColor = ColorHexParser.ResolveTheme(hi.ColorSubContent, hi.ColorSubContentDark, new Color4(0.6f, 0.6f, 0.6f, 1f));

        c.Node<Column>("highlight", col => { col.Spacing = 0f; }, () =>
        {
            c.Node<Stack>("highlightStack", static _ => { }, () =>
            {
                c.Node<Row>("main", r => { r.Gap = 12f; r.CrossAlignment = CrossAlignment.Start; }, () =>
                {
                    BitmapOrPlaceholder(c, item.IconBitmap, hi.PicFunction != null, effIconSize, Opacity);

                    c.Node<Column>("text", col2 => { col2.Spacing = 0f; col2.MaxChildWidth = textW; }, () =>
                    {
                        Line(c, "title", hi.Title, textW, 15f, DWriteFontWeight.Bold, 22f, titleColor);
                        if (hi.TimerInfo != null)
                        {
                            Line(c, "timer", SuperIslandCard.FormatDigitTimer(hi.TimerInfo), textW, 16f,
                                DWriteFontWeight.Normal, 22f, titleColor, Opacity, "Consolas");
                        }
                        if (!string.IsNullOrEmpty(hi.Content) && hi.Type != 1)
                            Line(c, "content", hi.Content, textW, 12f, DWriteFontWeight.Normal, 18f, contentColor);
                        Line(c, "sub", hi.SubContent, textW, 12f, DWriteFontWeight.Normal, 18f, subColor);
                    });
                });

                // 右侧大图：按现状 x 偏移绝对定位（左侧图 = cw - bigImages，右侧图贴右边缘）
                if (hasRight)
                {
                    float rightOffset = contentWidth - bigImages;
                    c.Node<Absolute>("bigRight", a => { a.Left = rightOffset; a.Top = 0f; }, () =>
                    {
                        c.Leaf<Bitmap>(null, b =>
                        {
                            b.Source = () => item.BigImageRightBitmap;
                            b.DrawSize = BigImageSize;
                            b.Opacity = Opacity;
                        });
                    });
                }
                if (hasLeft)
                {
                    float leftOffset = contentWidth - bigImages - (hasRight ? BigImageSize + BigImageGap : 0f);
                    c.Node<Absolute>("bigLeft", a => { a.Left = leftOffset; a.Top = 0f; }, () =>
                    {
                        c.Leaf<Bitmap>(null, b =>
                        {
                            b.Source = () => item.BigImageLeftBitmap;
                            b.DrawSize = BigImageSize;
                            b.Opacity = Opacity;
                        });
                    });
                }
            });

            // 现状高度 = max(effIconSize, 文本高) + 4
            c.Leaf<Spacer>("tail", sp => { sp.FixedHeight = 4f; });
        });
    }

    // ---------- 难：MultiProgress（Canvas 逃生节点） ----------

    public static void MultiProgress(OverlayComposer c, SuperIslandItem item, MultiProgressData mp, float contentWidth)
    {
        const float NodeSize = 55f;
        const float BarHeight = 8f;
        const float PointerSize = 47f;

        bool hasTitle = !string.IsNullOrEmpty(mp.Title);
        float canvasHeight = (hasTitle ? 12f : 0f) + NodeSize + 2f;

        c.Node<Column>("multiProgress", col => { col.Spacing = 0f; col.MaxChildWidth = contentWidth; }, () =>
        {
            if (hasTitle)
            {
                HtmlLine(c, "mpTitle", mp.Title, contentWidth, 14f, false, null, 12f, Opacity,
                    new Color4(1f, 1f, 1f, Opacity));
            }

            c.Leaf<Canvas>("mpCanvas", cv =>
            {
                cv.FixedSize = new Size(contentWidth, NodeSize + 2f);
                cv.OnPaint = (s, r) => PaintMultiProgress(s, r, item, mp, NodeSize, BarHeight, PointerSize);
            });
        });
    }
    private static void PaintMultiProgress(PaintScope s, Rect r, SuperIslandItem item, MultiProgressData mp,
        float nodeSize, float barHeight, float pointerSize)
    {
        float cx = r.X;
        float bottom = r.Y + nodeSize;

        int requested = mp.Points ?? 3;
        int nodeCount = Math.Max(1, requested);
        int segmentCount = Math.Max(1, nodeCount - 1);
        float progressValue = Math.Clamp(mp.Progress, 0, 100);
        float pct = progressValue / 100f;
        int pointerIndex = Math.Clamp((int)(pct * segmentCount), 0, nodeCount - 1);
        bool isFood = string.Equals(item.State.ParamV2?.Business, "food_delivery", StringComparison.OrdinalIgnoreCase);

        var trackColor = ColorHexParser.Parse(mp.Color) ?? new Color4(0.07f, 0.73f, 1f, 1f);   // 默认 #0ABAFF
        float barY = bottom - barHeight;

        // 进度条背景（primary 20% alpha）
        var trackBrush = s.BrushWithOpacity(new Color4(trackColor.R, trackColor.G, trackColor.B, 0.2f * Opacity));
        var trackRR = new RoundedRectangle(new RectangleF(cx, barY, r.Width, barHeight), barHeight / 2f, barHeight / 2f);
        s.Rt.FillRoundedRectangle(ref trackRR, trackBrush);

        // 进度条前景
        if (pct > 0f)
        {
            var fillBrush = s.BrushWithOpacity(new Color4(trackColor.R, trackColor.G, trackColor.B, Opacity));
            var fillRR = new RoundedRectangle(
                new RectangleF(cx, barY, MathF.Max(barHeight, r.Width * pct), barHeight), barHeight / 2f, barHeight / 2f);
            s.Rt.FillRoundedRectangle(ref fillRR, fillBrush);
        }

        // 节点行（底对齐、等距均分）
        if (requested > 0)
        {
            for (int i = 0; i < nodeCount; i++)
            {
                bool isLast = i == nodeCount - 1;
                bool isCompleted = i <= pointerIndex;
                bool nodeInvisible = i == 0 && isFood;
                if (nodeInvisible) continue;

                float nx = cx + (segmentCount > 0 ? i * (r.Width - nodeSize) / segmentCount : 0f);
                float ny = bottom - nodeSize;

                // 节点图标选择（对齐 Android MultiProgressCompose）
                Vortice.Direct2D1.ID2D1Bitmap? bmp;
                if (isLast && isCompleted) bmp = item.MultiEndBitmap ?? item.MultiMiddleBitmap;
                else if (isLast) bmp = item.MultiEndUnselBitmap ?? item.MultiMiddleUnselBitmap;
                else if (isCompleted) bmp = item.MultiMiddleBitmap ?? item.MultiForwardBoxBitmap;
                else bmp = item.MultiMiddleUnselBitmap ?? item.MultiForwardBoxBitmap;

                if (bmp != null)
                {
                    Bitmap.DrawScaled(s, bmp, nx, ny, nodeSize, Opacity);
                }
                else
                {
                    // 默认圆形指示器（nodeSize/4）
                    var dotBrush = s.BrushWithOpacity(isCompleted
                        ? new Color4(trackColor.R, trackColor.G, trackColor.B, Opacity)
                        : new Color4(trackColor.R, trackColor.G, trackColor.B, 0.3f * Opacity));
                    var center = new Vector2(nx + nodeSize / 2f, ny + nodeSize / 2f);
                    s.Rt.FillEllipse(new Ellipse(center, nodeSize / 4f, nodeSize / 4f), dotBrush);
                }
            }
        }

        // 进度指针（仅 1-99 显示，贴底悬浮最上层）
        if (progressValue >= 1 && progressValue <= 99 && requested > 0)
        {
            var pointerBmp = item.MultiForwardBitmap ?? item.MultiForwardBoxBitmap;
            float pointerHalf = pointerSize / 2f;
            float px = Math.Clamp(r.Width * pct, pointerHalf, MathF.Max(pointerHalf, r.Width - pointerHalf));
            float py = bottom - pointerSize;
            if (pointerBmp != null)
            {
                Bitmap.DrawScaled(s, pointerBmp, cx + px - pointerHalf, py, pointerSize, Opacity);
            }
            else
            {
                var ptrBrush = s.BrushWithOpacity(new Color4(trackColor.R, trackColor.G, trackColor.B, Opacity));
                var center = new Vector2(cx + px, py + pointerHalf);
                s.Rt.FillEllipse(new Ellipse(center, pointerHalf, pointerHalf), ptrBrush);
            }
        }
    }

    // ---------- 通用构件 ----------

    /// <summary>固定宽度、指定字号/权重/行高的单行文本（内容为空时跳过）。</summary>
    private static void Line(OverlayComposer c, string key, string? text, float width, float size,
        DWriteFontWeight weight, float lineHeight, Color4 color, float alphaScale = 1f, string fontFamily = "Microsoft YaHei")
    {
        if (string.IsNullOrEmpty(text)) return;
        c.Leaf<Text>(key, t =>
        {
            t.TextValue = text!;
            t.FontFamily = fontFamily;
            t.Weight = weight;
            t.FontSize = size;
            t.LineHeight = lineHeight;
            t.ReportedHeight = lineHeight;
            t.MaxWidth = width;
            t.Color = new Color4(color.R, color.G, color.B, color.A * alphaScale);
        });
    }

    /// <summary>
    /// HTML 着色文本行（支持 &lt;font color&gt; 分段着色，超宽回退整行截断）。
    /// 不透明度只由 <paramref name="alpha"/> 决定：基色与分段色都保持 RGB 语义（A=1），
    /// 由 <see cref="RichText.Opacity"/> 统一乘算，避免 alpha 被重复施加。
    /// </summary>
    private static void HtmlLine(OverlayComposer c, string key, string? text, float width, float size,
        bool bold, string? colorHex, float lineHeight, float alpha, Color4? fallbackColor = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var baseColor = ColorHexParser.Parse(colorHex)
            ?? fallbackColor
            ?? new Color4(1f, 1f, 1f, 1f);
        c.Leaf<RichText>(key, t =>
        {
            t.Html = text!;
            t.FontFamily = "Microsoft YaHei";
            t.Weight = bold ? DWriteFontWeight.Bold : DWriteFontWeight.Normal;
            t.FontSize = size;
            t.LineHeight = lineHeight;
            t.MaxWidth = width;
            t.BaseColor = new Color4(baseColor.R, baseColor.G, baseColor.B, 1f);
            t.Opacity = alpha;
        });
    }

    /// <summary>位图 + 可选的灰色圆形占位（图未加载成功但有图片键时）。</summary>
    private static void BitmapOrPlaceholder(OverlayComposer c, Vortice.Direct2D1.ID2D1Bitmap? bitmap,
        bool hasKey, float size, float opacity)
    {
        if (bitmap == null && !hasKey) return;
        c.Node<Box>(null, b => { b.Width = size; b.Height = size; }, () =>
        {
            c.Leaf<Bitmap>(null, b =>
            {
                b.Source = () => bitmap;
                b.DrawSize = size;
                b.Opacity = opacity;
                b.PlaceholderWhenEmpty = hasKey;
            });
        });
    }

    /// <summary>在指定像素 y 偏移处放置单行文本（复刻现状的 ty 绝对推进）。</summary>
    private static void OffsetText(OverlayComposer c, string key, float offsetY, Action<Text> configure)
    {
        c.Node<Column>(key, col => { col.Spacing = 0f; }, () =>
        {
            c.Leaf<Spacer>(null, sp => { sp.FixedHeight = offsetY; });
            c.Leaf<Text>(null, configure);
        });
    }
}
