namespace Errandesk.Server.Services;

/// <summary>
/// The server has no AI model configured, so the assistant cannot be used.
/// </summary>
public sealed class AssistantUnavailableException() : DomainException("The assistant is not available.", "The server has no AI model configured.")
{
    public override int StatusCode => StatusCodes.Status503ServiceUnavailable;
}
