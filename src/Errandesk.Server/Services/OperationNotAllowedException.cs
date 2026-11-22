namespace Errandesk.Server.Services;

/// <summary>
/// The server's policy does not allow the operation, such as registration while it is disabled.
/// </summary>
public sealed class OperationNotAllowedException(string detail) : DomainException("The operation is not allowed.", detail)
{
    public override int StatusCode => StatusCodes.Status403Forbidden;
}
