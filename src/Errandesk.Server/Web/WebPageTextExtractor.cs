using AngleSharp.Html.Parser;
using ReverseMarkdown;
using SmartReader;

namespace Errandesk.Server.Web;

/// <summary>
/// Extracts the readable text of an HTML page as Markdown: the main article when one is found, the whole body otherwise.
/// Scripts, styles, embedded frames and inline data images are left out.
/// </summary>
public static class WebPageTextExtractor
{
    private const string RemovedElementsSelector = "script, style, noscript, template, svg, canvas, iframe, object, embed, form, img[src^='data:']";

    public static WebPageDocument Extract(Uri url, string html)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var title = NormalizeTitle(document.Title);

        string contentHtml;
        var article = new Reader(url.AbsoluteUri, html).GetArticle();
        if (article.IsReadable && !string.IsNullOrWhiteSpace(article.Content))
        {
            title = NormalizeTitle(article.Title) ?? title;
            contentHtml = article.Content;
        }
        else contentHtml = document.Body?.InnerHtml ?? "";

        var contentDocument = parser.ParseDocument($"<body>{contentHtml}</body>");
        foreach (var element in contentDocument.QuerySelectorAll(RemovedElementsSelector).ToList()) element.Remove();

        var converter = new Converter(new Config { GithubFlavored = true, Tags = { Unknown = Config.UnknownTagsOption.Bypass }, Formatting = { RemoveComments = true }, Links = { SmartHref = true } });
        var markdown = converter.Convert(contentDocument.Body?.InnerHtml ?? "").Trim();
        return new WebPageDocument(url.AbsoluteUri, title, markdown);
    }

    private static string? NormalizeTitle(string? title) => string.IsNullOrWhiteSpace(title) ? null : title.Trim();
}
