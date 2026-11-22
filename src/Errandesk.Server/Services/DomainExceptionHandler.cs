using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Errandesk.Server.Services;

/// <summary>
/// Writes a <see cref="DomainException"/> as a ProblemDetails response with its status code, or a change version conflict as 409 with the current resource as the body.
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService, IOptions<HttpJsonOptions> jsonOptions) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException) return false;

        httpContext.Response.StatusCode = domainException.StatusCode;
        if (domainException is ChangeVersionConflictException conflictException)
        {
            await httpContext.Response.WriteAsJsonAsync(conflictException.CurrentResource, conflictException.CurrentResource.GetType(), jsonOptions.Value.SerializerOptions, cancellationToken);
            return true;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = domainException.StatusCode,
                Title = domainException.Title,
                Detail = domainException.Message,
            },
        });
    }
}
