using System.Text;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Errandesk.Client.Helpers;

/// <summary>
/// The Markdown dialect of notes and assistant messages, and plain-text helpers for it.
/// </summary>
/// <remarks>
/// The dialect is CommonMark with strikethrough, automatic links, pipe tables, task lists and emphasis next to CJK characters.
/// Raw HTML is shown as text, and every line break inside a paragraph is kept as a line break.
/// </remarks>
public static class MarkdownText
{
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseCjkFriendlyEmphasis()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    /// <summary>
    /// The text of <paramref name="markdown"/> without Markdown syntax, with runs of white space collapsed into single spaces and cut to <paramref name="maximumLength"/> characters.
    /// </summary>
    public static string ToExcerpt(string markdown, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var plainText = ToPlainText(markdown);
        var builder = new StringBuilder(Math.Min(plainText.Length, maximumLength));
        var isPreviousWhiteSpace = true;
        foreach (var character in plainText)
        {
            if (builder.Length >= maximumLength) break;
            if (char.IsWhiteSpace(character))
            {
                if (!isPreviousWhiteSpace) builder.Append(' ');
                isPreviousWhiteSpace = true;
            }
            else
            {
                builder.Append(character);
                isPreviousWhiteSpace = false;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The text of <paramref name="markdown"/> without Markdown syntax and without task list markers such as <c>[x]</c>.
    /// </summary>
    private static string ToPlainText(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var taskList in document.Descendants<TaskList>().ToList()) taskList.Remove();

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer) { EnableHtmlForBlock = false, EnableHtmlForInline = false, EnableHtmlEscape = false };
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>
    /// Converts Windows and classic Mac line endings into line feeds.
    /// </summary>
    public static string NormalizeLineEndings(string text) => text.Contains('\r') ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;
}
