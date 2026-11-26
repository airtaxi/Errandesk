namespace Errandesk.Server.Web;

/// <summary>
/// The whole extracted text of a web page, as kept between reads of its parts.
/// </summary>
public sealed record WebPageDocument(string Url, string? Title, string Text);
