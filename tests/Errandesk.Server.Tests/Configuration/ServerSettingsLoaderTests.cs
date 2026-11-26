using Errandesk.Server.Configuration;
using Errandesk.Server.Tests.Infrastructure;
using static Errandesk.Server.Configuration.ServerEnvironmentVariables;

namespace Errandesk.Server.Tests.Configuration;

public sealed class ServerSettingsLoaderTests
{
    private const string ConnectionString = "mongodb://localhost:27017";
    private const string SigningKey = "unit-test-signing-key-with-at-least-32-bytes";

    private readonly Dictionary<string, string?> _variables = new()
    {
        [MongoDbConnectionString] = ConnectionString,
        [JwtSigningKey] = SigningKey,
    };

    private readonly Dictionary<string, string> _files = [];

    [Fact]
    public void Load_WithRequiredValuesOnly_AppliesDefaults()
    {
        var settings = LoadValid();

        Assert.Equal(ConnectionString, settings.MongoDbConnectionString);
        Assert.Equal("errandesk", settings.MongoDbDatabaseName);
        Assert.Equal(SigningKey, settings.JwtSigningKey);
        Assert.Equal(TimeSpan.FromMinutes(15), settings.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(30), settings.RefreshTokenLifetime);
        Assert.Equal(8080, settings.HttpPort);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, settings.LogLevel);
        Assert.Equal(Contracts.RegistrationMode.Invite, settings.RegistrationMode);
        Assert.Null(settings.Ai);
        Assert.Null(settings.Fcm);
        Assert.Null(settings.Apns);
        Assert.Null(settings.MicrosoftGraph);
        Assert.False(settings.IsAiAvailable);
        Assert.False(settings.IsAndroidPushAvailable);
        Assert.False(settings.IsIosPushAvailable);
        Assert.False(settings.IsMicrosoftTodoImportAvailable);
    }

    [Fact]
    public void Load_WithOptionalValues_ParsesThem()
    {
        _variables[MongoDbDatabase] = "family";
        _variables[AccessTokenMinutes] = "5";
        _variables[RefreshTokenDays] = "90";
        _variables[HttpPort] = "5080";
        _variables[ServerEnvironmentVariables.LogLevel] = "debug";
        _variables[ServerEnvironmentVariables.RegistrationMode] = "OPEN";

        var settings = LoadValid();

        Assert.Equal("family", settings.MongoDbDatabaseName);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(90), settings.RefreshTokenLifetime);
        Assert.Equal(5080, settings.HttpPort);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, settings.LogLevel);
        Assert.Equal(Contracts.RegistrationMode.Open, settings.RegistrationMode);
    }

    [Fact]
    public void Load_WithBlankOptionalValues_TreatsThemAsUnset()
    {
        _variables[MongoDbDatabase] = "";
        _variables[HttpPort] = "   ";
        _variables[AiModel] = "";
        _variables[FcmProjectId] = "";

        var settings = LoadValid();

        Assert.Equal("errandesk", settings.MongoDbDatabaseName);
        Assert.Equal(8080, settings.HttpPort);
        Assert.Null(settings.Ai);
        Assert.Null(settings.Fcm);
    }

    [Fact]
    public void Load_WithoutRequiredValues_ReportsEachMissingVariable()
    {
        _variables.Clear();

        var errors = LoadErrors();

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Contains(MongoDbConnectionString, StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(JwtSigningKey, StringComparison.Ordinal));
    }

    [Fact]
    public void Load_WithShortSigningKey_ReportsErrorWithoutTheValue()
    {
        const string shortSigningKey = "short-secret-value";
        _variables[JwtSigningKey] = shortSigningKey;

        var error = Assert.Single(LoadErrors());

        Assert.Contains(JwtSigningKey, error, StringComparison.Ordinal);
        Assert.DoesNotContain(shortSigningKey, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:27017")]
    [InlineData("localhost:27017")]
    [InlineData("mongodb://")]
    public void Load_WithInvalidConnectionString_ReportsErrorWithoutTheValue(string connectionString)
    {
        _variables[MongoDbConnectionString] = connectionString;

        var error = Assert.Single(LoadErrors());

        Assert.Contains(MongoDbConnectionString, error, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithSrvConnectionString_AcceptsIt()
    {
        const string srvConnectionString = "mongodb+srv://user:password@cluster.example.com/";
        _variables[MongoDbConnectionString] = srvConnectionString;

        var loadResult = Load();

        Assert.DoesNotContain(loadResult.Errors, error => error.Contains("must start with", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpPort, "0")]
    [InlineData(HttpPort, "65536")]
    [InlineData(HttpPort, "eighty")]
    [InlineData(AccessTokenMinutes, "-1")]
    [InlineData(RefreshTokenDays, "1.5")]
    [InlineData(ServerEnvironmentVariables.LogLevel, "Verbose")]
    [InlineData(ServerEnvironmentVariables.LogLevel, "2")]
    [InlineData(ServerEnvironmentVariables.RegistrationMode, "closed")]
    [InlineData(MongoDbDatabase, "errandesk.production")]
    [InlineData(AiEndpoint, "ftp://models.example.com")]
    [InlineData(AiEndpoint, "not a url")]
    [InlineData(ServerEnvironmentVariables.ApnsEnvironment, "staging")]
    public void Load_WithInvalidValue_ReportsTheVariable(string name, string value)
    {
        _variables[name] = value;

        var error = Assert.Single(LoadErrors());

        Assert.Contains(name, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithSeveralProblems_ReportsAllOfThem()
    {
        _variables.Remove(JwtSigningKey);
        _variables[HttpPort] = "0";
        _variables[ServerEnvironmentVariables.RegistrationMode] = "closed";

        Assert.Equal(3, LoadErrors().Count);
    }

    [Fact]
    public void Load_WithSecretFromFile_UsesTrimmedFileContent()
    {
        _variables.Remove(JwtSigningKey);
        _variables[JwtSigningKey + FileSuffix] = "/run/secrets/jwt";
        _files["/run/secrets/jwt"] = $"  {SigningKey}{Environment.NewLine}";
        _variables.Remove(MongoDbConnectionString);
        _variables[MongoDbConnectionString + FileSuffix] = "/run/secrets/mongodb";
        _files["/run/secrets/mongodb"] = ConnectionString + "\n";

        var settings = LoadValid();

        Assert.Equal(SigningKey, settings.JwtSigningKey);
        Assert.Equal(ConnectionString, settings.MongoDbConnectionString);
    }

    [Fact]
    public void Load_WithSecretFromRealFile_ReadsIt()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filePath, SigningKey + "\n");
            _variables.Remove(JwtSigningKey);
            _variables[JwtSigningKey + FileSuffix] = filePath;

            var loadResult = new ServerSettingsLoader(name => _variables.GetValueOrDefault(name)).Load();

            Assert.True(loadResult.IsValid);
            Assert.Equal(SigningKey, loadResult.Settings.JwtSigningKey);
        }
        finally { File.Delete(filePath); }
    }

    [Fact]
    public void Load_WithBothValueAndFile_ReportsConflict()
    {
        _variables[JwtSigningKey + FileSuffix] = "/run/secrets/jwt";
        _files["/run/secrets/jwt"] = SigningKey;

        var error = Assert.Single(LoadErrors());

        Assert.Contains(JwtSigningKey, error, StringComparison.Ordinal);
        Assert.Contains(JwtSigningKey + FileSuffix, error, StringComparison.Ordinal);
        Assert.DoesNotContain(SigningKey, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithMissingSecretFile_ReportsTheFileVariable()
    {
        _variables.Remove(JwtSigningKey);
        _variables[JwtSigningKey + FileSuffix] = "/run/secrets/missing";

        var error = Assert.Single(LoadErrors());

        Assert.Contains(JwtSigningKey + FileSuffix, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithEmptySecretFile_ReportsTheFileVariable()
    {
        _variables.Remove(JwtSigningKey);
        _variables[JwtSigningKey + FileSuffix] = "/run/secrets/jwt";
        _files["/run/secrets/jwt"] = "  \n";

        var error = Assert.Single(LoadErrors());

        Assert.Contains(JwtSigningKey + FileSuffix, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithModelAndDefaultEndpointWithoutApiKey_ReportsMissingApiKey()
    {
        _variables[AiModel] = "gpt-5-mini";

        var error = Assert.Single(LoadErrors());

        Assert.Contains(AiApiKey, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithModelAndDefaultEndpointAndApiKey_EnablesAi()
    {
        _variables[AiModel] = "gpt-5-mini";
        _variables[AiApiKey] = "sk-test";

        var settings = LoadValid();

        Assert.True(settings.IsAiAvailable);
        Assert.Equal(ServerSettingsLoader.DefaultAiEndpoint, settings.Ai?.Endpoint);
        Assert.Equal("sk-test", settings.Ai?.ApiKey);
        Assert.Equal("gpt-5-mini", settings.Ai?.Model);
    }

    [Fact]
    public void Load_WithModelAndCustomEndpointWithoutApiKey_EnablesAiWithoutKey()
    {
        _variables[AiModel] = "llama3.2";
        _variables[AiEndpoint] = "http://localhost:11434/v1";

        var settings = LoadValid();

        Assert.True(settings.IsAiAvailable);
        Assert.Equal(new Uri("http://localhost:11434/v1"), settings.Ai?.Endpoint);
        Assert.Null(settings.Ai?.ApiKey);
    }

    [Fact]
    public void Load_WithApiKeyFromFile_UsesIt()
    {
        _variables[AiModel] = "gpt-5-mini";
        _variables[AiApiKey + FileSuffix] = "/run/secrets/ai";
        _files["/run/secrets/ai"] = "sk-from-file\n";

        Assert.Equal("sk-from-file", LoadValid().Ai?.ApiKey);
    }

    [Fact]
    public void Load_WithoutModel_DisablesAiEvenWithApiKey()
    {
        _variables[AiApiKey] = "sk-test";

        Assert.False(LoadValid().IsAiAvailable);
    }

    [Fact]
    public void Load_WithCompleteFcmGroup_EnablesAndroidPush()
    {
        var privateKey = TestKeys.CreateRsaPrivateKeyPem();
        _variables[FcmProjectId] = "errandesk-test";
        _variables[FcmClientEmail] = "push@errandesk-test.iam.gserviceaccount.com";
        _variables[FcmPrivateKey] = privateKey;

        var settings = LoadValid();

        Assert.True(settings.IsAndroidPushAvailable);
        Assert.Equal("errandesk-test", settings.Fcm?.ProjectId);
        Assert.Equal(privateKey.Trim().ReplaceLineEndings("\n"), settings.Fcm?.PrivateKey);
    }

    [Fact]
    public void Load_WithFcmKeyContainingEscapedLineBreaks_RestoresLineBreaks()
    {
        var privateKey = TestKeys.CreateRsaPrivateKeyPem().ReplaceLineEndings("\n");
        _variables[FcmProjectId] = "errandesk-test";
        _variables[FcmClientEmail] = "push@errandesk-test.iam.gserviceaccount.com";
        _variables[FcmPrivateKey] = privateKey.Replace("\n", "\\n", StringComparison.Ordinal);

        Assert.Equal(privateKey.Trim(), LoadValid().Fcm?.PrivateKey);
    }

    [Fact]
    public void Load_WithPartialFcmGroup_ReportsMissingVariables()
    {
        _variables[FcmProjectId] = "errandesk-test";

        var error = Assert.Single(LoadErrors());

        Assert.Contains(FcmClientEmail, error, StringComparison.Ordinal);
        Assert.Contains(FcmPrivateKey, error, StringComparison.Ordinal);
        Assert.DoesNotContain(FcmProjectId + ",", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithInvalidFcmPrivateKey_ReportsErrorWithoutTheValue()
    {
        const string invalidPrivateKey = "not-a-private-key";
        _variables[FcmProjectId] = "errandesk-test";
        _variables[FcmClientEmail] = "push@errandesk-test.iam.gserviceaccount.com";
        _variables[FcmPrivateKey] = invalidPrivateKey;

        var error = Assert.Single(LoadErrors());

        Assert.Contains(FcmPrivateKey, error, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidPrivateKey, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithCompleteApnsGroup_EnablesIosPush()
    {
        _variables[ApnsTeamId] = "TEAM123456";
        _variables[ApnsKeyId] = "KEY1234567";
        _variables[ApnsPrivateKey + FileSuffix] = "/run/secrets/apns.p8";
        _files["/run/secrets/apns.p8"] = TestKeys.CreateEllipticCurvePrivateKeyPem();
        _variables[ApnsBundleId] = "com.airtaxi.errandesk";
        _variables[ServerEnvironmentVariables.ApnsEnvironment] = "sandbox";

        var settings = LoadValid();

        Assert.True(settings.IsIosPushAvailable);
        Assert.Equal("TEAM123456", settings.Apns?.TeamId);
        Assert.Equal("KEY1234567", settings.Apns?.KeyId);
        Assert.Equal("com.airtaxi.errandesk", settings.Apns?.BundleId);
        Assert.Equal(Server.Configuration.ApnsEnvironment.Sandbox, settings.Apns?.Environment);
    }

    [Fact]
    public void Load_WithApnsGroupWithoutEnvironment_DefaultsToProduction()
    {
        _variables[ApnsTeamId] = "TEAM123456";
        _variables[ApnsKeyId] = "KEY1234567";
        _variables[ApnsPrivateKey] = TestKeys.CreateEllipticCurvePrivateKeyPem();
        _variables[ApnsBundleId] = "com.airtaxi.errandesk";

        Assert.Equal(Server.Configuration.ApnsEnvironment.Production, LoadValid().Apns?.Environment);
    }

    [Fact]
    public void Load_WithPartialApnsGroup_ReportsMissingVariables()
    {
        _variables[ApnsTeamId] = "TEAM123456";
        _variables[ApnsBundleId] = "com.airtaxi.errandesk";

        var error = Assert.Single(LoadErrors());

        Assert.Contains(ApnsKeyId, error, StringComparison.Ordinal);
        Assert.Contains(ApnsPrivateKey, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithApnsEnvironmentOnly_KeepsIosPushDisabled()
    {
        _variables[ServerEnvironmentVariables.ApnsEnvironment] = "sandbox";

        Assert.False(LoadValid().IsIosPushAvailable);
    }

    [Fact]
    public void Load_WithRsaKeyForApns_ReportsInvalidKey()
    {
        _variables[ApnsTeamId] = "TEAM123456";
        _variables[ApnsKeyId] = "KEY1234567";
        _variables[ApnsPrivateKey] = TestKeys.CreateRsaPrivateKeyPem();
        _variables[ApnsBundleId] = "com.airtaxi.errandesk";

        var error = Assert.Single(LoadErrors());

        Assert.Contains(ApnsPrivateKey, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithMicrosoftGraphClientId_EnablesImportWithDefaultTenant()
    {
        _variables[MicrosoftGraphClientId] = "00000000-0000-0000-0000-000000000001";

        var settings = LoadValid();

        Assert.True(settings.IsMicrosoftTodoImportAvailable);
        Assert.Equal("common", settings.MicrosoftGraph?.Tenant);
    }

    [Fact]
    public void Load_WithMicrosoftGraphTenant_UsesIt()
    {
        _variables[MicrosoftGraphClientId] = "00000000-0000-0000-0000-000000000001";
        _variables[MicrosoftGraphTenant] = "consumers";

        Assert.Equal("consumers", LoadValid().MicrosoftGraph?.Tenant);
    }

    [Fact]
    public void Load_WithoutClientUpdateBaseUrl_LeavesClientUpdatesOff() => Assert.Null(LoadValid().ClientUpdateBaseUrl);

    [Fact]
    public void Load_WithBlankClientUpdateBaseUrl_LeavesClientUpdatesOff()
    {
        _variables[ClientUpdateBaseUrl] = "   ";

        Assert.Null(LoadValid().ClientUpdateBaseUrl);
    }

    [Theory]
    [InlineData("https://updates.example.com/errandesk", "https://updates.example.com/errandesk/")]
    [InlineData("http://192.168.0.10:8081/clients/", "http://192.168.0.10:8081/clients/")]
    public void Load_WithClientUpdateBaseUrl_NormalizesTrailingSlash(string value, string expected)
    {
        _variables[ClientUpdateBaseUrl] = value;

        Assert.Equal(expected, LoadValid().ClientUpdateBaseUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("updates.example.com")]
    [InlineData("ftp://updates.example.com/")]
    [InlineData("https://updates.example.com/?channel=beta")]
    [InlineData("https://updates.example.com/?")]
    [InlineData("https://updates.example.com/#")]
    public void Load_WithInvalidClientUpdateBaseUrl_Fails(string value)
    {
        _variables[ClientUpdateBaseUrl] = value;

        var error = Assert.Single(LoadErrors());
        Assert.Contains(ClientUpdateBaseUrl, error, StringComparison.Ordinal);
    }

    private ServerSettingsLoadResult Load() => new ServerSettingsLoader(name => _variables.GetValueOrDefault(name), ReadFile).Load();

    private ServerSettings LoadValid()
    {
        var loadResult = Load();
        Assert.True(loadResult.IsValid, string.Join(Environment.NewLine, loadResult.Errors));
        return loadResult.Settings;
    }

    private IReadOnlyList<string> LoadErrors()
    {
        var loadResult = Load();
        Assert.False(loadResult.IsValid);
        return loadResult.Errors;
    }

    private string ReadFile(string path) => _files.TryGetValue(path, out var content) ? content : throw new FileNotFoundException("The file does not exist.", path);
}
