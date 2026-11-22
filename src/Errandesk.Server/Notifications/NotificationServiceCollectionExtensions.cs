using Errandesk.Server.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Errandesk.Server.Notifications;

public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reminder dispatcher and the morning briefing scheduler as hosted services, plus the FCM sender when Android push is configured and the APNs sender when iOS push is configured.
    /// </summary>
    public static IServiceCollection AddErrandeskNotifications(this IServiceCollection services, ServerSettings settings)
    {
        services.AddSingleton<ReminderDispatcherOptions>();
        services.AddSingleton<ReminderDispatcher>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ReminderDispatcher>());
        services.TryAddSingleton<MorningBriefingOptions>();
        services.AddSingleton<MorningBriefingScheduler>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MorningBriefingScheduler>());

        if (settings.Fcm is { } fcmSettings) services.AddSingleton<IPushNotificationSender>(serviceProvider => new FcmPushNotificationSender(fcmSettings, serviceProvider.GetRequiredService<ILogger<FcmPushNotificationSender>>()));

        if (settings.Apns is { } apnsSettings)
        {
            services.AddHttpClient(ApnsPushNotificationSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromHours(1), EnableMultipleHttp2Connections = true }).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            services.AddSingleton(serviceProvider => new ApnsProviderTokenGenerator(apnsSettings, serviceProvider.GetRequiredService<TimeProvider>()));
            services.AddSingleton<IPushNotificationSender>(serviceProvider => new ApnsPushNotificationSender(serviceProvider.GetRequiredService<IHttpClientFactory>(), apnsSettings, serviceProvider.GetRequiredService<ApnsProviderTokenGenerator>(), serviceProvider.GetRequiredService<TimeProvider>(), serviceProvider.GetRequiredService<ILogger<ApnsPushNotificationSender>>()));
        }
        return services;
    }
}
