namespace Errandesk.Server.Web;

/// <summary>
/// One part of the text of a web page.
/// </summary>
/// <param name="Url">The address the page was finally read from, after redirects.</param>
/// <param name="Title">The page title, or <see langword="null"/> when the page has none.</param>
/// <param name="Content">The part of the page text, as Markdown for HTML pages.</param>
/// <param name="StartIndex">Position of the part in the whole text.</param>
/// <param name="TotalLength">Length of the whole text.</param>
/// <param name="NextStartIndex">Position of the next part, or <see langword="null"/> when this part reaches the end.</param>
public sealed record WebPageContent(string Url, string? Title, string Content, int StartIndex, int TotalLength, int? NextStartIndex);
