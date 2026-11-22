using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Errandesk.Server.Configuration;

namespace Errandesk.Server.Import;

/// <summary>
/// Signs a user in to Microsoft with the OAuth 2.0 device authorization grant of the Microsoft identity platform, as a public client.
/// </summary>
public sealed class MicrosoftIdentityClient(IHttpClientFactory httpClientFactory, ServerSettings settings)
{
    public const string HttpClientName = "microsoft-identity";

    /// <summary>Read access to To Do, plus a refresh token so an import can outlast the access token.</summary>
    public const string Scopes = "Tasks.Read offline_access";

    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    /// <summary>Starts a sign-in and returns the code the user enters at the verification address.</summary>
    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        var response = await PostAsync("devicecode", [new("client_id", GetSettings().ClientId), new("scope", Scopes)], cancellationToken);
        var deviceCode = await ReadAsync(response, MicrosoftGraphJsonSerializerContext.Default.DeviceCodeResponse, cancellationToken);
        if (!response.IsSuccessStatusCode || deviceCode is not { DeviceCode.Length: > 0, UserCode.Length: > 0, VerificationUri.Length: > 0, ExpiresIn: > 0 }) throw new MicrosoftServiceException($"Microsoft sign-in answered with status {(int)response.StatusCode}.");
        return deviceCode;
    }

    /// <summary>Asks once whether the user finished signing in with <paramref name="deviceCode"/>.</summary>
    public async Task<DeviceCodeTokenResult> PollTokenAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var response = await PostAsync("token", [new("grant_type", DeviceCodeGrantType), new("client_id", GetSettings().ClientId), new("device_code", deviceCode)], cancellationToken);
        var token = await ReadAsync(response, MicrosoftGraphJsonSerializerContext.Default.TokenEndpointResponse, cancellationToken);
        if (response.IsSuccessStatusCode && token is { AccessToken.Length: > 0 }) return new DeviceCodeTokenResult(DeviceCodeTokenStatus.Succeeded, new MicrosoftTokens(token.AccessToken, token.RefreshToken));

        return token?.Error switch
        {
            "authorization_pending" => new DeviceCodeTokenResult(DeviceCodeTokenStatus.Pending),
            "slow_down" => new DeviceCodeTokenResult(DeviceCodeTokenStatus.SlowDown),
            "authorization_declined" or "access_denied" => new DeviceCodeTokenResult(DeviceCodeTokenStatus.Declined),
            "expired_token" or "code_expired" => new DeviceCodeTokenResult(DeviceCodeTokenStatus.Expired),
            _ => throw new MicrosoftServiceException($"Microsoft sign-in answered with status {(int)response.StatusCode}."),
        };
    }

    /// <summary>Exchanges a refresh token for new tokens.</summary>
    public async Task<MicrosoftTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var response = await PostAsync("token", [new("grant_type", "refresh_token"), new("client_id", GetSettings().ClientId), new("refresh_token", refreshToken), new("scope", Scopes)], cancellationToken);
        var token = await ReadAsync(response, MicrosoftGraphJsonSerializerContext.Default.TokenEndpointResponse, cancellationToken);
        if (!response.IsSuccessStatusCode || token is not { AccessToken.Length: > 0 }) throw new MicrosoftServiceException("The Microsoft sign-in expired during the import.");
        return new MicrosoftTokens(token.AccessToken, token.RefreshToken ?? refreshToken);
    }

    private async Task<HttpResponseMessage> PostAsync(string endpointName, IEnumerable<KeyValuePair<string, string>> formFields, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var endpoint = new Uri($"https://login.microsoftonline.com/{Uri.EscapeDataString(GetSettings().Tenant)}/oauth2/v2.0/{endpointName}");
        using var content = new FormUrlEncodedContent(formFields);
        return await httpClient.PostAsync(endpoint, content, cancellationToken);
    }

    private static async Task<TValue?> ReadAsync<TValue>(HttpResponseMessage response, JsonTypeInfo<TValue> typeInfo, CancellationToken cancellationToken)
    {
        using (response)
        {
            try { return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken); }
            catch (JsonException) { return default; }
            catch (NotSupportedException) { return default; }
        }
    }

    private MicrosoftGraphSettings GetSettings() => settings.MicrosoftGraph ?? throw new InvalidOperationException("Microsoft Graph is not configured.");
}
