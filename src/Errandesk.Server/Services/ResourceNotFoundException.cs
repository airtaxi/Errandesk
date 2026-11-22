namespace Errandesk.Server.Services;

/// <summary>
/// The resource does not exist, is deleted, or belongs to another user.
/// </summary>
public sealed class ResourceNotFoundException(string resourceName) : DomainException("The resource was not found.", $"The {resourceName} was not found.")
{
    public override int StatusCode => StatusCodes.Status404NotFound;
}
