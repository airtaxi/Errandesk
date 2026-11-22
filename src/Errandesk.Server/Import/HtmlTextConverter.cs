using System.Net;
using System.Text.RegularExpressions;

namespace Errandesk.Server.Import;

/// <summary>
/// Turns the HTML body of an imported item into plain text: block ends and line breaks become new lines, other tags are removed and entities decoded.
/// </summary>
public static partial class HtmlTextConverter
{
    public static string ToPlainText(string html)
    {
        var text = HiddenContentRegex().Replace(html, "");
        text = LineBreakRegex().Replace(text, "\n");
        text = BlockEndRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        text = TrailingSpaceRegex().Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), "\n");
        return ExtraBlankLinesRegex().Replace(text, "\n\n").Trim();
    }

    [GeneratedRegex(@"<(head|style|script)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HiddenContentRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"</(p|div|li|h[1-6]|tr|blockquote|pre)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLinesRegex();
}
