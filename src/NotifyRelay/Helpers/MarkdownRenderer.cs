using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI;
using Microsoft.UI.Xaml.Documents;

namespace NotifyRelay.Helpers;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline? _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static void RenderToInlines(string markdown, InlineCollection inlines, Brush? defaultForeground = null, double defaultFontSize = 16)
    {
        inlines.Clear();

        if (string.IsNullOrEmpty(markdown))
            return;

        var document = Markdown.Parse(markdown, _pipeline);
        RenderBlock(document, inlines, defaultForeground, defaultFontSize);
    }

    private static void RenderBlock(Markdig.Syntax.MarkdownObject block, InlineCollection inlines, Brush? defaultForeground, double defaultFontSize)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                RenderInlineContainer(paragraph.Inline, inlines, defaultForeground, defaultFontSize);
                inlines.Add(new LineBreak());
                break;

            case HeadingBlock heading:
                var headingSize = heading.Level switch
                {
                    1 => defaultFontSize * 2.0,
                    2 => defaultFontSize * 1.5,
                    3 => defaultFontSize * 1.25,
                    _ => defaultFontSize
                };
                var headingRun = new Run { Text = GetLiteralText(heading.Inline), FontSize = headingSize, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                inlines.Add(headingRun);
                inlines.Add(new LineBreak());
                break;

            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    RenderBlock(child, inlines, defaultForeground, defaultFontSize);
                }
                break;

            case ListBlock list:
                var isOrdered = list.IsOrdered;
                var index = 1;
                foreach (var item in list)
                {
                    if (item is ListItemBlock listItem)
                    {
                        var marker = isOrdered ? $"{index}. " : "• ";
                        inlines.Add(new Run { Text = marker });
                        foreach (var child in listItem)
                        {
                            RenderBlock(child, inlines, defaultForeground, defaultFontSize);
                        }
                        index++;
                    }
                }
                break;

            case HtmlBlock:
            case FencedCodeBlock:
            case CodeBlock:
            case ThematicBreakBlock:
                inlines.Add(new LineBreak());
                break;

            case ContainerBlock container:
                foreach (var child in container)
                {
                    RenderBlock(child, inlines, defaultForeground, defaultFontSize);
                }
                break;
        }
    }

    private static string GetLiteralText(ContainerInline? container)
    {
        if (container == null) return string.Empty;

        var result = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    result.Append(literal.Content.ToString());
                    break;
                case ContainerInline ci:
                    result.Append(GetLiteralText(ci));
                    break;
            }
        }
        return result.ToString();
    }

    private static void RenderInlineContainer(ContainerInline? container, InlineCollection inlines, Brush? defaultForeground, double defaultFontSize)
    {
        if (container == null) return;

        foreach (var inline in container)
        {
            RenderInline(inline, inlines, defaultForeground, defaultFontSize);
        }
    }

    private static void RenderInline(Markdig.Syntax.Inlines.Inline inline, Microsoft.UI.Xaml.Documents.InlineCollection inlines, Brush? defaultForeground, double defaultFontSize)
    {
        switch (inline)
        {
            case LiteralInline literal:
                inlines.Add(new Run { Text = literal.Content.ToString(), Foreground = defaultForeground ?? new SolidColorBrush(Colors.White) });
                break;

            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;

            case EmphasisInline emphasis:
                var delimiter = emphasis.DelimiterChar;
                var count = emphasis.DelimiterCount;

                if (count >= 2 && (delimiter == '*' || delimiter == '_'))
                {
                    var bold = new Bold();
                    foreach (var child in emphasis)
                    {
                        RenderInline(child, bold.Inlines, defaultForeground, defaultFontSize);
                    }
                    inlines.Add(bold);
                }
                else if ((delimiter == '*' || delimiter == '_') && count == 1)
                {
                    var italic = new Italic();
                    foreach (var child in emphasis)
                    {
                        RenderInline(child, italic.Inlines, defaultForeground, defaultFontSize);
                    }
                    inlines.Add(italic);
                }
                else
                {
                    foreach (var child in emphasis)
                    {
                        RenderInline(child, inlines, defaultForeground, defaultFontSize);
                    }
                }
                break;

            case LinkInline link:
                inlines.Add(new Run { Text = link.Title ?? link.Url, Foreground = new SolidColorBrush(Colors.CornflowerBlue) });
                break;

            case CodeInline code:
                inlines.Add(new Run { Text = code.Content.ToString(), FontFamily = new FontFamily("Consolas") });
                break;

            case AutolinkInline autolink:
                inlines.Add(new Run { Text = autolink.Url, Foreground = new SolidColorBrush(Colors.CornflowerBlue) });
                break;

            case ContainerInline container:
                RenderInlineContainer(container, inlines, defaultForeground, defaultFontSize);
                break;
        }
    }
}
