namespace Errandesk.Server.Services;

/// <summary>
/// An import cannot start because the server is not configured for it or the other service cannot be reached.
/// </summary>
public sealed class ImportUnavailableException(string detail) : DomainException("The import is not available.", detail)
{
    public override int StatusCode => StatusCodes.Status503ServiceUnavailable;
}
