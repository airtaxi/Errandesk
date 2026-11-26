using Errandesk.Server.Services;

namespace Errandesk.Server.Web;

/// <summary>
/// A web page could not be read, for example because it is blocked, too large, of an unsupported type, or did not answer in time.
/// </summary>
public sealed class WebPageUnavailableException(string detail) : DomainException("The web page could not be read.", detail)
{
    public override int StatusCode => StatusCodes.Status502BadGateway;
}
