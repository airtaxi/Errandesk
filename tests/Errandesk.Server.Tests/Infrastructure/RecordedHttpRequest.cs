namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// An HTTP request seen by <see cref="RecordingHttpMessageHandler"/>.
/// </summary>
public sealed record RecordedHttpRequest(HttpMethod Method, Uri Uri, Version Version, HttpVersionPolicy VersionPolicy, IReadOnlyDictionary<string, string> Headers, string? ContentType, string Body);
