using MongoDB.Driver;

namespace Errandesk.Server.Data;

public static class MongoExceptionExtensions
{
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>
    /// Whether the exception reports a duplicate key, optionally only on the index with the given name.
    /// </summary>
    public static bool IsDuplicateKey(this MongoException exception, string? indexName = null)
    {
        var message = exception switch
        {
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } writeException => writeException.WriteError.Message,
            MongoCommandException { Code: DuplicateKeyErrorCode } commandException => commandException.ErrorMessage,
            _ => null,
        };
        return message is not null && (indexName is null || message.Contains($"index: {indexName} ", StringComparison.Ordinal));
    }
}
