using System.Net;

namespace Errandesk.Client.Services;

/// <summary>
/// The server answered a request with an error status.
/// </summary>
public sealed class ApiException(HttpStatusCode statusCode, string? problemDetail) : Exception(problemDetail ?? $"The server responded with status {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>
    /// The <c>detail</c> of the ProblemDetails response body, when the server sent one.
    /// </summary>
    public string? ProblemDetail { get; } = problemDetail;
}
