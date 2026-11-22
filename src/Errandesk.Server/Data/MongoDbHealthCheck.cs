using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Errandesk.Server.Data;

/// <summary>
/// Reports healthy when MongoDB answers a <c>ping</c> command.
/// </summary>
public sealed class MongoDbHealthCheck(IMongoDatabase database) : IHealthCheck
{
    private static readonly BsonDocument s_pingCommand = new("ping", 1);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.RunCommandAsync<BsonDocument>(s_pingCommand, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is MongoException or TimeoutException) { return HealthCheckResult.Unhealthy("MongoDB did not answer the ping command.", exception); }
    }
}
