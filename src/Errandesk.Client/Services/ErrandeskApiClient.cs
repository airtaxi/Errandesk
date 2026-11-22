using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Errandesk.Contracts;

namespace Errandesk.Client.Services;

/// <summary>
/// Calls the anonymous endpoints of the Errandesk server API: server information, registration, sign-in, token refresh and sign-out.
/// </summary>
public sealed class ErrandeskApiClient(HttpClient httpClient)
{
    private static ErrandeskJsonSerializerContext JsonContext => ErrandeskJsonSerializerContext.Default;

    /// <summary>
    /// Reads the server information from <c>api/server-info</c> below <paramref name="serverAddress"/>.
    /// </summary>
    /// <exception cref="HttpRequestException">The server could not be reached or answered with an error status.</exception>
    /// <exception cref="TaskCanceledException">The request timed out.</exception>
    /// <exception cref="InvalidDataException">The response is not Errandesk server information.</exception>
    public async Task<ServerInfoResponse> GetServerInfoAsync(Uri serverAddress, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(serverAddress, "api/server-info");
        ServerInfoResponse? serverInfo;
        try { serverInfo = await httpClient.GetFromJsonAsync(requestUri, JsonContext.ServerInfoResponse, cancellationToken); }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException) { throw new InvalidDataException("The response is not Errandesk server information.", exception); }

        return serverInfo is null || string.IsNullOrWhiteSpace(serverInfo.ServerVersion) ? throw new InvalidDataException("The response is not Errandesk server information.") : serverInfo;
    }

    /// <exception cref="ApiException">The server rejected the registration.</exception>
    public Task<AuthenticationResponse> RegisterAsync(Uri serverAddress, RegisterRequest request, CancellationToken cancellationToken) => PostAsync(serverAddress, "api/auth/register", request, JsonContext.RegisterRequest, JsonContext.AuthenticationResponse, cancellationToken);

    /// <exception cref="ApiException">The server rejected the credentials.</exception>
    public Task<AuthenticationResponse> LoginAsync(Uri serverAddress, LoginRequest request, CancellationToken cancellationToken) => PostAsync(serverAddress, "api/auth/login", request, JsonContext.LoginRequest, JsonContext.AuthenticationResponse, cancellationToken);

    /// <exception cref="ApiException">The refresh token is no longer valid.</exception>
    public Task<AuthenticationResponse> RefreshAsync(Uri serverAddress, string refreshToken, CancellationToken cancellationToken) => PostAsync(serverAddress, "api/auth/refresh", new RefreshTokenRequest(refreshToken), JsonContext.RefreshTokenRequest, JsonContext.AuthenticationResponse, cancellationToken);

    /// <summary>
    /// Ends the session of <paramref name="refreshToken"/> on the server.
    /// </summary>
    public async Task LogoutAsync(Uri serverAddress, string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(new Uri(serverAddress, "api/auth/logout"), JsonContent.Create(new RefreshTokenRequest(refreshToken), JsonContext.RefreshTokenRequest), cancellationToken);
        await ApiResponseReader.EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(Uri serverAddress, string path, TRequest request, JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(new Uri(serverAddress, path), JsonContent.Create(request, requestTypeInfo), cancellationToken);
        return await ApiResponseReader.ReadAsync(response, responseTypeInfo, cancellationToken);
    }
}
