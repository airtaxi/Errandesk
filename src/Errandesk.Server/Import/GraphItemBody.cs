namespace Errandesk.Server.Import;

/// <summary>
/// The body of a Microsoft Graph item (<c>itemBody</c>).
/// </summary>
/// <param name="ContentType"><c>text</c> or <c>html</c>.</param>
public sealed record GraphItemBody(string? Content, string? ContentType)
{
    public bool IsHtml => string.Equals(ContentType, "html", StringComparison.OrdinalIgnoreCase);
}
