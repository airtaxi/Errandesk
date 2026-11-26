using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Errandesk.Contracts;
using MongoDB.Driver;
using static Errandesk.Server.Configuration.ServerEnvironmentVariables;

namespace Errandesk.Server.Configuration;

/// <summary>
/// Reads and validates <see cref="ServerSettings"/> from environment variables.
/// </summary>
/// <remarks>
/// Blank values count as unset. Secret variables can also be supplied through a companion <c>_FILE</c> variable that names a file whose trimmed content is the value.
/// </remarks>
/// <param name="readVariable">Returns the value of an environment variable, or <see langword="null"/> when it is not set.</param>
/// <param name="readFile">Returns the full text content of a file.</param>
public sealed class ServerSettingsLoader(Func<string, string?> readVariable, Func<string, string> readFile)
{
    public const string DefaultMongoDbDatabaseName = "errandesk";
    public const int DefaultAccessTokenMinutes = 15;
    public const int DefaultRefreshTokenDays = 30;
    public const int DefaultHttpPort = 8080;
    public const string DefaultMicrosoftGraphTenant = "common";
    public const int MinimumJwtSigningKeyByteCount = 32;

    public static readonly Uri DefaultAiEndpoint = new("https://api.openai.com/v1");

    private const string DefaultAiEndpointHost = "api.openai.com";
    private const int MaximumMongoDbDatabaseNameLength = 63;

    private static readonly char[] s_invalidMongoDbDatabaseNameCharacters = ['/', '\\', '.', ' ', '"', '$', '*', '<', '>', ':', '|', '?'];

    public ServerSettingsLoader(Func<string, string?> readVariable) : this(readVariable, File.ReadAllText) { }

    /// <summary>Creates a loader that reads the environment variables of the current process.</summary>
    public static ServerSettingsLoader FromEnvironment() => new(Environment.GetEnvironmentVariable);

    /// <summary>Reads every setting, validates all of them and reports every problem at once.</summary>
    public ServerSettingsLoadResult Load()
    {
        var errors = new List<string>();

        var mongoDbConnectionString = ReadRequiredSecret(MongoDbConnectionString, errors);
        if (mongoDbConnectionString is not null) ValidateMongoDbConnectionString(mongoDbConnectionString, errors);

        var mongoDbDatabaseName = ReadValue(MongoDbDatabase) ?? DefaultMongoDbDatabaseName;
        ValidateMongoDbDatabaseName(mongoDbDatabaseName, errors);

        var jwtSigningKey = ReadRequiredSecret(JwtSigningKey, errors);
        if (jwtSigningKey is not null && Encoding.UTF8.GetByteCount(jwtSigningKey) < MinimumJwtSigningKeyByteCount) errors.Add($"{JwtSigningKey} must be at least {MinimumJwtSigningKeyByteCount} bytes long in UTF-8.");

        var accessTokenMinutes = ReadInteger(AccessTokenMinutes, DefaultAccessTokenMinutes, 1, 1440, errors);
        var refreshTokenDays = ReadInteger(RefreshTokenDays, DefaultRefreshTokenDays, 1, 3650, errors);
        var httpPort = ReadInteger(HttpPort, DefaultHttpPort, 1, 65535, errors);
        var logLevel = ReadLogLevel(errors);
        var registrationMode = ReadRegistrationMode(errors);
        var aiSettings = ReadAiSettings(errors);
        var fcmSettings = ReadFcmSettings(errors);
        var apnsSettings = ReadApnsSettings(errors);
        var microsoftGraphSettings = ReadMicrosoftGraphSettings();
        var clientUpdateBaseUrl = ReadClientUpdateBaseUrl(errors);

        if (errors.Count > 0 || mongoDbConnectionString is null || jwtSigningKey is null) return ServerSettingsLoadResult.Failure(errors);

        return ServerSettingsLoadResult.Success(new ServerSettings
        {
            MongoDbConnectionString = mongoDbConnectionString,
            MongoDbDatabaseName = mongoDbDatabaseName,
            JwtSigningKey = jwtSigningKey,
            AccessTokenLifetime = TimeSpan.FromMinutes(accessTokenMinutes),
            RefreshTokenLifetime = TimeSpan.FromDays(refreshTokenDays),
            HttpPort = httpPort,
            LogLevel = logLevel,
            RegistrationMode = registrationMode,
            Ai = aiSettings,
            Fcm = fcmSettings,
            Apns = apnsSettings,
            MicrosoftGraph = microsoftGraphSettings,
            ClientUpdateBaseUrl = clientUpdateBaseUrl,
        });
    }

    private string? ReadValue(string name)
    {
        var value = readVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Reads a secret from the variable itself or from the file named by its <c>_FILE</c> companion.</summary>
    /// <returns><see langword="false"/> when the variable pair is misconfigured; the problem is added to <paramref name="errors"/>.</returns>
    private bool TryReadSecret(string name, List<string> errors, out string? value)
    {
        value = null;
        var fileVariableName = name + FileSuffix;
        var directValue = ReadValue(name);
        var filePath = ReadValue(fileVariableName);

        if (directValue is not null && filePath is not null)
        {
            errors.Add($"{name} and {fileVariableName} are both set; set only one of them.");
            return false;
        }

        if (filePath is null)
        {
            value = directValue;
            return true;
        }

        string fileContent;
        try { fileContent = readFile(filePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add($"{fileVariableName} names the file '{filePath}', which could not be read: {exception.GetType().Name}.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(fileContent))
        {
            errors.Add($"{fileVariableName} names the file '{filePath}', which is empty.");
            return false;
        }

        value = fileContent.Trim();
        return true;
    }

    private string? ReadRequiredSecret(string name, List<string> errors)
    {
        if (!TryReadSecret(name, errors, out var value)) return null;
        if (value is null) errors.Add($"{name} (or {name}{FileSuffix}) is required.");
        return value;
    }

    private int ReadInteger(string name, int defaultValue, int minimum, int maximum, List<string> errors)
    {
        var value = ReadValue(name);
        if (value is null) return defaultValue;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum) return number;

        errors.Add($"{name} must be a whole number from {minimum} to {maximum}.");
        return defaultValue;
    }

    private bool ReadBoolean(string name, bool defaultValue, List<string> errors)
    {
        var value = ReadValue(name);
        if (value is null) return defaultValue;
        if (bool.TryParse(value, out var flag)) return flag;

        errors.Add($"{name} must be true or false.");
        return defaultValue;
    }

    private LogLevel ReadLogLevel(List<string> errors)
    {
        var value = ReadValue(ServerEnvironmentVariables.LogLevel);
        if (value is null) return Microsoft.Extensions.Logging.LogLevel.Information;

        var names = Enum.GetNames<LogLevel>();
        var matchingName = names.FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
        if (matchingName is not null) return Enum.Parse<LogLevel>(matchingName);

        errors.Add($"{ServerEnvironmentVariables.LogLevel} must be one of: {string.Join(", ", names)}.");
        return Microsoft.Extensions.Logging.LogLevel.Information;
    }

    private RegistrationMode ReadRegistrationMode(List<string> errors)
    {
        var value = ReadValue(ServerEnvironmentVariables.RegistrationMode);
        switch (value?.ToLowerInvariant())
        {
            case null:
            case "invite":
                return Contracts.RegistrationMode.Invite;
            case "open":
                return Contracts.RegistrationMode.Open;
            case "disabled":
                return Contracts.RegistrationMode.Disabled;
            default:
                errors.Add($"{ServerEnvironmentVariables.RegistrationMode} must be one of: open, invite, disabled.");
                return Contracts.RegistrationMode.Invite;
        }
    }

    private AiSettings? ReadAiSettings(List<string> errors)
    {
        var endpoint = DefaultAiEndpoint;
        var endpointValue = ReadValue(AiEndpoint);
        if (endpointValue is not null)
        {
            if (Uri.TryCreate(endpointValue, UriKind.Absolute, out var parsedEndpoint) && (parsedEndpoint.Scheme == Uri.UriSchemeHttp || parsedEndpoint.Scheme == Uri.UriSchemeHttps)) endpoint = parsedEndpoint;
            else errors.Add($"{AiEndpoint} must be an absolute http or https URL.");
        }

        var isApiKeyValid = TryReadSecret(AiApiKey, errors, out var apiKey);
        var isWebFetchEnabled = ReadBoolean(AiWebFetchEnabled, true, errors);
        var allowsPrivateNetworkWebFetch = ReadBoolean(AiWebFetchAllowPrivateNetworks, false, errors);
        var model = ReadValue(AiModel);
        if (model is null) return null;

        if (isApiKeyValid && apiKey is null && string.Equals(endpoint.Host, DefaultAiEndpointHost, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{AiApiKey} (or {AiApiKey}{FileSuffix}) is required when {AiModel} is set and {AiEndpoint} is the OpenAI endpoint.");
            return null;
        }

        return new AiSettings { Endpoint = endpoint, ApiKey = apiKey, Model = model, IsWebFetchEnabled = isWebFetchEnabled, AllowsPrivateNetworkWebFetch = allowsPrivateNetworkWebFetch };
    }

    private FcmSettings? ReadFcmSettings(List<string> errors)
    {
        var projectId = ReadValue(FcmProjectId);
        var clientEmail = ReadValue(FcmClientEmail);
        if (!TryReadSecret(FcmPrivateKey, errors, out var privateKey)) return null;

        if (!IsGroupComplete("Android push (FCM)", [(FcmProjectId, projectId), (FcmClientEmail, clientEmail), (FcmPrivateKey, privateKey)], errors)) return null;
        if (projectId is null || clientEmail is null || privateKey is null) return null;

        privateKey = NormalizePrivateKey(privateKey);
        if (!IsValidRsaPrivateKey(privateKey))
        {
            errors.Add($"{FcmPrivateKey} must be a PEM-encoded RSA private key.");
            return null;
        }

        if (!clientEmail.Contains('@'))
        {
            errors.Add($"{FcmClientEmail} must be an email address.");
            return null;
        }

        return new FcmSettings { ProjectId = projectId, ClientEmail = clientEmail, PrivateKey = privateKey };
    }

    private ApnsSettings? ReadApnsSettings(List<string> errors)
    {
        var environment = ApnsEnvironment.Production;
        var environmentValue = ReadValue(ServerEnvironmentVariables.ApnsEnvironment);
        switch (environmentValue?.ToLowerInvariant())
        {
            case null:
            case "production":
                break;
            case "sandbox":
                environment = ApnsEnvironment.Sandbox;
                break;
            default:
                errors.Add($"{ServerEnvironmentVariables.ApnsEnvironment} must be one of: production, sandbox.");
                break;
        }

        var teamId = ReadValue(ApnsTeamId);
        var keyId = ReadValue(ApnsKeyId);
        var bundleId = ReadValue(ApnsBundleId);
        if (!TryReadSecret(ApnsPrivateKey, errors, out var privateKey)) return null;

        if (!IsGroupComplete("iOS push (APNs)", [(ApnsTeamId, teamId), (ApnsKeyId, keyId), (ApnsPrivateKey, privateKey), (ApnsBundleId, bundleId)], errors)) return null;
        if (teamId is null || keyId is null || privateKey is null || bundleId is null) return null;

        privateKey = NormalizePrivateKey(privateKey);
        if (!IsValidEllipticCurvePrivateKey(privateKey))
        {
            errors.Add($"{ApnsPrivateKey} must be the PEM content of an APNs .p8 key.");
            return null;
        }

        return new ApnsSettings { TeamId = teamId, KeyId = keyId, PrivateKey = privateKey, BundleId = bundleId, Environment = environment };
    }

    private MicrosoftGraphSettings? ReadMicrosoftGraphSettings()
    {
        var clientId = ReadValue(MicrosoftGraphClientId);
        if (clientId is null) return null;

        var tenant = ReadValue(MicrosoftGraphTenant) ?? DefaultMicrosoftGraphTenant;
        return new MicrosoftGraphSettings { ClientId = clientId, Tenant = tenant };
    }

    /// <summary>Reads the client update base URL; the returned URL always ends with a slash so relative paths resolve below it.</summary>
    private Uri? ReadClientUpdateBaseUrl(List<string> errors)
    {
        var value = ReadValue(ClientUpdateBaseUrl);
        if (value is null) return null;

        var baseUrl = ClientUpdateLayout.NormalizeBaseUrl(value);
        if (baseUrl is null) errors.Add($"{ClientUpdateBaseUrl} must be an absolute http or https URL without a query or fragment.");
        return baseUrl;
    }

    /// <summary>Accepts a group that is either completely set or completely unset, and reports a partially configured group.</summary>
    private static bool IsGroupComplete(string groupName, (string Name, string? Value)[] variables, List<string> errors)
    {
        var missingNames = variables.Where(variable => variable.Value is null).Select(variable => variable.Name).ToArray();
        if (missingNames.Length == 0) return true;
        if (missingNames.Length == variables.Length) return false;

        errors.Add($"{groupName} is partially configured. Missing: {string.Join(", ", missingNames)}. Set every variable of the group or none of them.");
        return false;
    }

    private static void ValidateMongoDbConnectionString(string connectionString, List<string> errors)
    {
        if (!connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) && !connectionString.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{MongoDbConnectionString} must start with mongodb:// or mongodb+srv://.");
            return;
        }

        try { _ = MongoUrl.Create(connectionString); }
        catch (Exception exception) when (exception is MongoConfigurationException or ArgumentException or FormatException) { errors.Add($"{MongoDbConnectionString} is not a valid MongoDB connection string."); }
    }

    private static void ValidateMongoDbDatabaseName(string databaseName, List<string> errors)
    {
        if (databaseName.Length > MaximumMongoDbDatabaseNameLength || databaseName.IndexOfAny(s_invalidMongoDbDatabaseNameCharacters) >= 0)
        {
            errors.Add($"{MongoDbDatabase} must be at most {MaximumMongoDbDatabaseNameLength} characters and must not contain any of: / \\ . space \" $ * < > : | ?");
        }
    }

    /// <summary>Turns escaped <c>\n</c> sequences, as found in JSON service account files, into real line breaks.</summary>
    private static string NormalizePrivateKey(string privateKey) => privateKey.Replace("\\n", "\n", StringComparison.Ordinal).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static bool IsValidRsaPrivateKey(string privateKey)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey);
            _ = rsa.ExportParameters(true);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException) { return false; }
    }

    private static bool IsValidEllipticCurvePrivateKey(string privateKey)
    {
        try
        {
            using var ellipticCurve = ECDsa.Create();
            ellipticCurve.ImportFromPem(privateKey);
            _ = ellipticCurve.ExportParameters(true);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException) { return false; }
    }
}
