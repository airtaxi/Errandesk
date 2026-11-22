namespace Errandesk.Server.Services;

/// <summary>
/// The request conflicts with existing data, such as a taken user name or an ID used by another resource.
/// </summary>
public sealed class ResourceConflictException(string detail) : DomainException("The request conflicts with existing data.", detail)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
}
