namespace Errandesk.Client.Sync;

/// <summary>
/// The server's answer to a request of the synchronization.
/// </summary>
/// <param name="StatusCode">HTTP status code.</param>
/// <param name="Body">Response body, or <see langword="null"/> when it is empty.</param>
/// <param name="HasRetryAfter">Whether the response carries a <c>Retry-After</c> header, which marks a conflict that resolves itself shortly.</param>
public sealed record SyncTransportResponse(int StatusCode, string? Body, bool HasRetryAfter = false)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
