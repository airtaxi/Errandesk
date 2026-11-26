namespace Errandesk.Server.Configuration;

/// <summary>
/// Names of the environment variables that configure the server.
/// </summary>
public static class ServerEnvironmentVariables
{
    /// <summary>Suffix of the companion variable that names a file holding a secret value.</summary>
    public const string FileSuffix = "_FILE";

    public const string MongoDbConnectionString = "ERRANDESK_MONGODB_CONNECTION_STRING";
    public const string MongoDbDatabase = "ERRANDESK_MONGODB_DATABASE";
    public const string JwtSigningKey = "ERRANDESK_JWT_SIGNING_KEY";
    public const string AccessTokenMinutes = "ERRANDESK_ACCESS_TOKEN_MINUTES";
    public const string RefreshTokenDays = "ERRANDESK_REFRESH_TOKEN_DAYS";
    public const string HttpPort = "ERRANDESK_HTTP_PORT";
    public const string LogLevel = "ERRANDESK_LOG_LEVEL";
    public const string RegistrationMode = "ERRANDESK_REGISTRATION_MODE";
    public const string AiEndpoint = "ERRANDESK_AI_ENDPOINT";
    public const string AiApiKey = "ERRANDESK_AI_API_KEY";
    public const string AiModel = "ERRANDESK_AI_MODEL";
    public const string AiWebFetchEnabled = "ERRANDESK_AI_WEB_FETCH_ENABLED";
    public const string AiWebFetchAllowPrivateNetworks = "ERRANDESK_AI_WEB_FETCH_ALLOW_PRIVATE_NETWORKS";
    public const string FcmProjectId = "ERRANDESK_FCM_PROJECT_ID";
    public const string FcmClientEmail = "ERRANDESK_FCM_CLIENT_EMAIL";
    public const string FcmPrivateKey = "ERRANDESK_FCM_PRIVATE_KEY";
    public const string ApnsTeamId = "ERRANDESK_APNS_TEAM_ID";
    public const string ApnsKeyId = "ERRANDESK_APNS_KEY_ID";
    public const string ApnsPrivateKey = "ERRANDESK_APNS_PRIVATE_KEY";
    public const string ApnsBundleId = "ERRANDESK_APNS_BUNDLE_ID";
    public const string ApnsEnvironment = "ERRANDESK_APNS_ENVIRONMENT";
    public const string MicrosoftGraphClientId = "ERRANDESK_MICROSOFT_GRAPH_CLIENT_ID";
    public const string MicrosoftGraphTenant = "ERRANDESK_MICROSOFT_GRAPH_TENANT";
    public const string ClientUpdateBaseUrl = "ERRANDESK_CLIENT_UPDATE_BASE_URL";
}
