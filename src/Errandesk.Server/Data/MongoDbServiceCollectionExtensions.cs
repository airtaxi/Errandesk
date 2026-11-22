using Errandesk.Server.Configuration;
using MongoDB.Driver;

namespace Errandesk.Server.Data;

public static class MongoDbServiceCollectionExtensions
{
    /// <summary>
    /// Registers BSON serialization, the singleton <see cref="IMongoClient"/>, the configured <see cref="IMongoDatabase"/>, typed collection access, the change counter and the index initializer.
    /// </summary>
    public static IServiceCollection AddErrandeskMongoDb(this IServiceCollection services, ServerSettings settings)
    {
        MongoDbSerialization.Register();

        services.AddSingleton<IMongoClient>(_ => new MongoClient(settings.MongoDbConnectionString));
        services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IMongoClient>().GetDatabase(settings.MongoDbDatabaseName));
        services.AddSingleton<ErrandeskDatabase>();
        services.AddSingleton<ChangeVersionGenerator>();
        services.AddSingleton<IMongoIndexCreator, AccountIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, TaskIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, NoteIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, AssistantIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, ReminderIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, SyncIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, ImportIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, ApiTokenIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, MorningBriefingIndexCreator>();
        services.AddSingleton<IMongoIndexCreator, SharingIndexCreator>();
        services.AddSingleton<MongoIndexInitializer>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MongoIndexInitializer>());
        return services;
    }
}
