namespace Errandesk.Server.Services;

/// <summary>
/// The request contains an invalid value or asks for an operation the resource does not allow.
/// </summary>
public sealed class RequestValidationException(string detail) : DomainException("The request is invalid.", detail)
{
    public override int StatusCode => StatusCodes.Status400BadRequest;
}
