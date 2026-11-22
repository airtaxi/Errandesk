using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Errandesk.Server.Configuration;
using Errandesk.Server.Notifications;
using Errandesk.Server.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace Errandesk.Server.Tests.Notifications;

public sealed class ApnsProviderTokenGeneratorTests
{
    private static readonly DateTimeOffset s_startTime = new(2031, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetToken_CreatesAnEs256TokenWithKeyIdTeamIdAndIssueTime()
    {
        var privateKey = TestKeys.CreateEllipticCurvePrivateKeyPem();
        using var generator = new ApnsProviderTokenGenerator(CreateSettings(privateKey), new FakeTimeProvider(s_startTime));

        var token = generator.GetToken();

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("KEY1234567", header.RootElement.GetProperty("kid").GetString());
        Assert.Equal(2, header.RootElement.EnumerateObject().Count());
        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        Assert.Equal("TEAM123456", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal(s_startTime.ToUnixTimeSeconds(), claims.RootElement.GetProperty("iat").GetInt64());

        using var verificationKey = ECDsa.Create();
        verificationKey.ImportFromPem(privateKey);
        var signature = Base64Url.DecodeFromChars(parts[2]);
        Assert.Equal(64, signature.Length);
        Assert.True(verificationKey.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void GetToken_ReusesTheTokenForFiftyMinutesAndAfterInvalidationCreatesANewOne()
    {
        var timeProvider = new FakeTimeProvider(s_startTime);
        using var generator = new ApnsProviderTokenGenerator(CreateSettings(TestKeys.CreateEllipticCurvePrivateKeyPem()), timeProvider);

        var firstToken = generator.GetToken();
        timeProvider.Advance(TimeSpan.FromMinutes(49));
        var reusedToken = generator.GetToken();
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var renewedToken = generator.GetToken();
        generator.Invalidate(firstToken);
        var afterUnrelatedInvalidation = generator.GetToken();
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        generator.Invalidate(renewedToken);
        var afterInvalidation = generator.GetToken();

        Assert.Equal(firstToken, reusedToken);
        Assert.NotEqual(firstToken, renewedToken);
        Assert.Equal(s_startTime.AddMinutes(50).ToUnixTimeSeconds(), ReadIssuedAt(renewedToken));
        Assert.Equal(renewedToken, afterUnrelatedInvalidation);
        Assert.NotEqual(renewedToken, afterInvalidation);
        Assert.Equal(s_startTime.AddMinutes(51).ToUnixTimeSeconds(), ReadIssuedAt(afterInvalidation));
    }

    public static ApnsSettings CreateSettings(string privateKey, ApnsEnvironment environment = ApnsEnvironment.Production) => new()
    {
        TeamId = "TEAM123456",
        KeyId = "KEY1234567",
        PrivateKey = privateKey,
        BundleId = "com.example.errandesk",
        Environment = environment
    };

    public static long ReadIssuedAt(string token)
    {
        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(token.Split('.')[1]));
        return claims.RootElement.GetProperty("iat").GetInt64();
    }
}
