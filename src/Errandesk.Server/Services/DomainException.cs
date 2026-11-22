namespace Errandesk.Server.Services;

/// <summary>
/// An expected failure of a domain operation that maps to an HTTP status code and a ProblemDetails response.
/// </summary>
public abstract class DomainException(string title, string detail) : Exception(detail)
{
    public string Title { get; } = title;

    public abstract int StatusCode { get; }
}
