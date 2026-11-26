using System.Text.Json;
using System.Text.Json.Serialization;
using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Errandesk.Server.Authentication;
using Errandesk.Server.Configuration;
using Errandesk.Server.Data;
using Errandesk.Server.Import;
using Errandesk.Server.Mcp;
using Errandesk.Server.Notifications;
using Errandesk.Server.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Errandesk.Server;

/// <summary>
/// Composes the web application from validated <see cref="ServerSettings"/>.
/// </summary>
public static class ErrandeskServerApplication
{
    public const string HealthPath = "/health";

    private static readonly TimeSpan s_healthCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Creates a builder with every configuration source removed, Kestrel listening on all interfaces at the configured port, and all services registered.
    /// </summary>
    public static WebApplicationBuilder CreateBuilder(WebApplicationOptions options, ServerSettings settings)
    {
        var builder = WebApplication.CreateBuilder(options);
        builder.Configuration.Sources.Clear();

        builder.Logging.SetMinimumLevel(settings.LogLevel);
        if (settings.LogLevel == LogLevel.Information)
        {
            builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        }

        builder.WebHost.ConfigureKestrel(kestrelOptions => kestrelOptions.ListenAnyIP(settings.HttpPort));

        builder.Services.AddSingleton(settings);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddErrandeskMongoDb(settings);
        builder.Services.AddErrandeskAuthentication(settings);
        builder.Services.AddSingleton<ServerInfoService>();
        builder.Services.AddSingleton<TaskListAccess>();
        builder.Services.AddSingleton<DocumentUpdater>();
        builder.Services.AddSingleton<UserService>();
        builder.Services.AddSingleton<InviteService>();
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<TaskListService>();
        builder.Services.AddSingleton<NoteService>();
        builder.Services.AddSingleton<TaskService>();
        builder.Services.AddSingleton<ChangeNotifier>();
        builder.Services.AddSingleton<ChangeFeedService>();
        builder.Services.AddSingleton<IdempotencyKeyStore>();
        builder.Services.AddSingleton<TombstonePurgeOptions>();
        builder.Services.AddSingleton<TombstonePurger>();
        builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<TombstonePurger>());
        builder.Services.AddSingleton<EventStreamOptions>();
        builder.Services.AddSingleton<AgentActionService>();
        builder.Services.AddSingleton<ConversationService>();
        builder.Services.AddSingleton<ConversationAttachmentService>();
        builder.Services.AddSingleton<AgentToolsetFactory>();
        builder.Services.AddSingleton<AssistantConversationRunner>();
        builder.Services.AddSingleton<MorningBriefingService>();
        builder.Services.AddSingleton<MorningBriefingWriter>();
        builder.Services.AddSingleton<DeviceService>();
        builder.Services.AddErrandeskNotifications(settings);
        builder.Services.AddErrandeskImport();
        builder.Services.AddErrandeskMcp();
        if (settings.Ai is { } aiSettings) builder.Services.AddSingleton(_ => AssistantChatClientFactory.Create(aiSettings));

        builder.Services.AddControllers().AddApplicationPart(typeof(ErrandeskServerApplication).Assembly).AddJsonOptions(jsonOptions => ConfigureJsonSerializerOptions(jsonOptions.JsonSerializerOptions));
        builder.Services.ConfigureHttpJsonOptions(jsonOptions => ConfigureJsonSerializerOptions(jsonOptions.SerializerOptions));
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();
        builder.Services.AddExceptionHandler<DatabaseUnavailableExceptionHandler>();
        builder.Services.AddOpenApi();
        builder.Services.AddHealthChecks().AddCheck<MongoDbHealthCheck>("mongodb", timeout: s_healthCheckTimeout);

        return builder;
    }

    /// <summary>
    /// Builds the application and configures its request pipeline.
    /// </summary>
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var application = builder.Build();

        application.UseExceptionHandler();
        application.UseStatusCodePages();
        if (application.Environment.IsDevelopment()) application.MapOpenApi();

        application.UseAuthentication();
        application.UseAuthorization();
        application.UseRateLimiter();
        application.UseMiddleware<IdempotencyMiddleware>();
        application.MapHealthChecks(HealthPath).AllowAnonymous();
        application.MapControllers();
        application.MapErrandeskMcp();

        return application;
    }

    private static void ConfigureJsonSerializerOptions(JsonSerializerOptions serializerOptions)
    {
        serializerOptions.TypeInfoResolverChain.Insert(0, ErrandeskJsonSerializerContext.Default);
        serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }
}
