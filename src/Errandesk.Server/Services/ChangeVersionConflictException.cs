namespace Errandesk.Server.Services;

/// <summary>
/// The resource no longer has the change version the client expected. The response body is the current resource.
/// </summary>
public sealed class ChangeVersionConflictException(object currentResource) : DomainException("The resource has changed.", "The resource was changed since the expected change version.")
{
    public object CurrentResource { get; } = currentResource;

    public override int StatusCode => StatusCodes.Status409Conflict;
}
