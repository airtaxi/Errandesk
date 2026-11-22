using Microsoft.AspNetCore.Diagnostics;
using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Turns MongoDB connection failures and server selection timeouts into a 503 ProblemDetails response.
/// </summary>
public sealed partial class DatabaseUnavailableExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<DatabaseUnavailableExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not (MongoConnectionException or TimeoutException)) return false;

        LogDatabaseUnavailable(logger, exception);
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "The database is unavailable.",
                Detail = "The server could not reach its database. Try again later.",
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A request failed because MongoDB is unavailable.")]
    private static partial void LogDatabaseUnavailable(ILogger logger, Exception exception);
}
