using System.Diagnostics.CodeAnalysis;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.Text;
using MarkdownBlock = Markdig.Syntax.Block;
using MarkdownInline = Markdig.Syntax.Inlines.Inline;
using XamlInline = Microsoft.UI.Xaml.Documents.Inline;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Turns Markdown into XAML elements: headings, paragraphs with emphasis, strikethrough, inline code and links, lists and task lists,
/// quotes, code blocks, tables and dividers. Only http, https and mailto links can be opened.
/// </summary>
/// <remarks>
/// Uses only core controls and text elements (StackPanel, Grid, Border, ContentPresenter, ScrollViewer, TextBlock inlines and Hyperlink),
/// so the output looks the same with native WinUI rendering and with the Skia renderer. Colors come from styles in App.xaml that use theme resources.
/// </remarks>
public static class MarkdownElementBuilder
{
    public const double BlockSpacing = 12;

    private const double ListItemSpacing = 4;
    private const double ListMarkerWidth = 20;
    private const double BulletSize = 5;
    private const string UncheckedTaskGlyph = "\uE739";
    private const string CheckedTaskGlyph = "\uE73A";

    private static readonly double[] s_headingFontSizes = [24, 20, 17, 15, 14, 14];
    private static FontFamily? s_codeFontFamily;

    private static FontFamily CodeFontFamily => s_codeFontFamily ??= new FontFamily(GetCodeFontFamilyName());

    /// <summary>
    /// Parses <paramref name="markdown"/> and adds one element per top-level block to <paramref name="elements"/>.
    /// </summary>
    public static void AppendBlocks(UIElementCollection elements, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;
        AppendBlocks(elements, Markdown.Parse(markdown, MarkdownText.Pipeline));
    }

    private static void AppendBlocks(UIElementCollection elements, ContainerBlock container)
    {
        foreach (var block in container)
        {
            if (CreateBlockElement(block) is { } element)
            {
                elements.Add(element);
            }
        }
    }

    private static UIElement? CreateBlockElement(MarkdownBlock block) => block switch
    {
        HeadingBlock heading => CreateHeading(heading),
        ParagraphBlock paragraph => CreateParagraph(paragraph),
        CodeBlock code => CreateCodeBlock(code),
        ThematicBreakBlock => new Rectangle { Style = GetStyle("MarkdownDividerStyle") },
        ListBlock list => CreateList(list),
        QuoteBlock quote => CreateQuote(quote),
        Table table => CreateTable(table),
        LinkReferenceDefinitionGroup => null,
        ContainerBlock container => CreateStack(container, BlockSpacing),
        LeafBlock leaf => CreateParagraph(leaf),
        _ => null,
    };

    private static TextBlock CreateTextBlock() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true
    };

    private static TextBlock CreateParagraph(LeafBlock leaf)
    {
        var textBlock = CreateTextBlock();
        if (leaf.Inline is { } inline) AppendInlines(textBlock.Inlines, inline, false);
        else textBlock.Text = leaf.Lines.ToString();
        return textBlock;
    }

    private static TextBlock CreateHeading(HeadingBlock heading)
    {
        var level = Math.Clamp(heading.Level, 1, s_headingFontSizes.Length);
        var textBlock = CreateParagraph(heading);
        textBlock.FontSize = s_headingFontSizes[level - 1];
        textBlock.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(textBlock, (AutomationHeadingLevel)level);
        return textBlock;
    }

    private static StackPanel CreateStack(ContainerBlock container, double spacing)
    {
        var stackPanel = new StackPanel { Spacing = spacing };
        AppendBlocks(stackPanel.Children, container);
        return stackPanel;
    }

    private static Border CreateCodeBlock(CodeBlock code)
    {
        var textBlock = new TextBlock { Text = code.Lines.ToString().TrimEnd('\n', '\r'), FontFamily = CodeFontFamily, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.NoWrap };
        return new Border { Style = GetStyle("MarkdownCodeBlockBorderStyle"), Child = CreateHorizontalScrollViewer(textBlock) };
    }

    private static ScrollViewer CreateHorizontalScrollViewer(UIElement content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Disabled,
    };

    private static ContentPresenter CreateQuote(QuoteBlock quote) => new()
    {
        Style = GetStyle("MarkdownQuoteStyle"),
        Content = CreateStack(quote, BlockSpacing)
    };

    private static StackPanel CreateList(ListBlock list)
    {
        var stackPanel = new StackPanel { Spacing = ListItemSpacing };
        var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var grid = new Grid { ColumnSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ListMarkerWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var content = CreateStack(item, ListItemSpacing);
            Grid.SetColumn(content, 1);
            grid.Children.Add(CreateListMarker(list, item, number++));
            grid.Children.Add(content);
            stackPanel.Children.Add(grid);
        }

        return stackPanel;
    }

    private static FrameworkElement CreateListMarker(ListBlock list, ListItemBlock item, int number)
    {
        if (GetTaskListState(item) is { } isChecked) return new FontIcon { Glyph = isChecked ? CheckedTaskGlyph : UncheckedTaskGlyph, Style = GetStyle("MarkdownTaskMarkerStyle") };
        if (list.IsOrdered) return new TextBlock { Text = $"{number}{list.OrderedDelimiter}" };
        return new Ellipse { Width = BulletSize, Height = BulletSize, Style = GetStyle("MarkdownBulletStyle") };
    }

    /// <summary>
    /// Whether a list item starts with a task marker such as <c>[x]</c>, and whether it is checked.
    /// </summary>
    private static bool? GetTaskListState(ListItemBlock item) => item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList taskList } ? taskList.Checked : null;

    private static ScrollViewer CreateTable(Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columnCount = Math.Max(table.ColumnDefinitions.Count, rows.Count == 0 ? 0 : rows.Max(row => row.Count));
        var grid = new Grid();
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[rowIndex];
            var columnIndex = 0;
            foreach (var cell in row.OfType<TableCell>())
            {
                if (columnIndex >= columnCount) break;
                var cellPresenter = new ContentPresenter
                {
                    Style = GetStyle("MarkdownTableCellStyle"),
                    Content = CreateStack(cell, ListItemSpacing),
                    BorderThickness = new Thickness(0, 0, columnIndex < columnCount - 1 ? 1 : 0, rowIndex < rows.Count - 1 ? 1 : 0),
                    FontWeight = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal,
                };
                Grid.SetRow(cellPresenter, rowIndex);
                Grid.SetColumn(cellPresenter, columnIndex);
                grid.Children.Add(cellPresenter);
                columnIndex++;
            }
        }

        var border = new Border { Style = GetStyle("MarkdownTableBorderStyle"), Child = grid };
        return CreateHorizontalScrollViewer(border);
    }

    /// <summary>
    /// Adds the XAML inlines of <paramref name="container"/>. Inside a link, nested links become plain spans and line breaks become spaces,
    /// because a Hyperlink cannot contain another Hyperlink or a LineBreak.
    /// </summary>
    private static void AppendInlines(InlineCollection target, ContainerInline container, bool isInsideLink)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            if (CreateInline(inline, isInsideLink) is { } xamlInline)
            {
                target.Add(xamlInline);
            }
        }
    }

    private static XamlInline? CreateInline(MarkdownInline inline, bool isInsideLink) => inline switch
    {
        LiteralInline literal => new Run { Text = literal.PreviousSibling is TaskList ? literal.Content.ToString().TrimStart() : literal.Content.ToString() },
        CodeInline code => new Run { Text = code.Content, FontFamily = CodeFontFamily },
        LineBreakInline => isInsideLink ? new Run { Text = " " } : new LineBreak(),
        HtmlEntityInline entity => new Run { Text = entity.Transcoded.ToString() },
        HtmlInline html => new Run { Text = html.Tag },
        AutolinkInline autolink => CreateLink(autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url, autolink.Url, isInsideLink),
        LinkInline link => CreateLink(link, isInsideLink),
        EmphasisInline emphasis => CreateEmphasis(emphasis, isInsideLink),
        TaskList => null,
        ContainerInline container => CreateSpan(container, isInsideLink),
        _ => null,
    };

    private static Span CreateEmphasis(EmphasisInline emphasis, bool isInsideLink)
    {
        Span span = emphasis.DelimiterChar == '~' ? new Span { TextDecorations = TextDecorations.Strikethrough } : emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
        AppendInlines(span.Inlines, emphasis, isInsideLink);
        return span;
    }

    private static Span CreateSpan(ContainerInline container, bool isInsideLink)
    {
        var span = new Span();
        AppendInlines(span.Inlines, container, isInsideLink);
        return span;
    }

    private static Span CreateLink(string url, string text, bool isInsideLink)
    {
        var span = CreateLinkSpan(url, isInsideLink);
        span.Inlines.Add(new Run { Text = text });
        return span;
    }

    /// <summary>
    /// A link, or for an image the image's description as a link to the image.
    /// </summary>
    private static Span CreateLink(LinkInline link, bool isInsideLink)
    {
        var span = CreateLinkSpan(link.Url, isInsideLink);
        AppendInlines(span.Inlines, link, true);
        if (span.Inlines.Count == 0) span.Inlines.Add(new Run { Text = link.Url ?? string.Empty });
        return span;
    }

    private static Span CreateLinkSpan(string? url, bool isInsideLink) => !isInsideLink && TryCreateNavigableUri(url, out var uri) ? new Hyperlink { NavigateUri = uri } : new Span();

    private static bool TryCreateNavigableUri(string? url, [NotNullWhen(true)] out Uri? uri) => Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https" or "mailto";

    private static Style GetStyle(string key) => (Style)Application.Current.Resources[key];

    private static string GetCodeFontFamilyName() => OperatingSystem.IsWindows() ? "Consolas" : OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ? "Menlo" : "monospace";
}
