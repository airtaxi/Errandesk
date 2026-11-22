using System.Net;
using System.Text.Json;
using Errandesk.Client.Helpers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.Managers;

/// <summary>
/// Holds the signed-in session: the access token in memory, the refresh token in secure storage and the signed-in user.
/// Refreshes run one at a time, because the server revokes a session whose refresh token is presented twice.
/// </summary>
public sealed class SessionManager(ErrandeskApiClient apiClient, ApplicationSettingsService settingsService, RefreshTokenStore refreshTokenStore)
{
    private static readonly TimeSpan s_accessTokenRenewalMargin = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    /// <summary>
    /// Raised when the session could not be renewed during a request and the user was signed out.
    /// </summary>
    public event EventHandler? SessionEnded;

    /// <summary>
    /// Raised when the signed-in user's profile changes.
    /// </summary>
    public event EventHandler? CurrentUserChanged;

    public UserResponse? CurrentUser { get; private set; }

    public bool HasStoredSession => refreshTokenStore.Read() is not null;

    /// <summary>
    /// The saved server address.
    /// </summary>
    /// <exception cref="InvalidOperationException">No valid server address is saved.</exception>
    public Uri ServerAddress => TryGetServerAddress(out var serverAddress) ? serverAddress : throw new InvalidOperationException("No server address is saved.");

    public bool TryGetServerAddress(out Uri serverAddress)
    {
        if (ServerAddressHelper.TryNormalize(settingsService.ServerAddress, out var normalizedAddress))
        {
            serverAddress = normalizedAddress;
            return true;
        }

        serverAddress = null!;
        return false;
    }

    /// <summary>
    /// Resumes the stored session by refreshing its tokens.
    /// </summary>
    /// <returns><see langword="true"/> when the session was resumed; <see langword="false"/> when there is no valid stored session.</returns>
    /// <exception cref="HttpRequestException">The server could not be reached.</exception>
    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken)
    {
        if (refreshTokenStore.Read() is not { } refreshToken) return false;

        await _refreshGate.WaitAsync(cancellationToken);
        try { return await RefreshCoreAsync(refreshToken, cancellationToken); }
        finally { _refreshGate.Release(); }
    }

    public async Task SignInAsync(string userName, string password, CancellationToken cancellationToken) => ApplyAuthentication(await apiClient.LoginAsync(ServerAddress, new LoginRequest(userName, password, DeviceInformationHelper.GetDeviceName()), cancellationToken));

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken) => ApplyAuthentication(await apiClient.RegisterAsync(ServerAddress, request, cancellationToken));

    /// <summary>
    /// Continues the stored session without the server: the user saved at the last sign-in on this server becomes the current user,
    /// and the access token is requested when the server can be reached again.
    /// </summary>
    /// <returns><see langword="true"/> when a stored session and its user exist.</returns>
    public bool TryResumeOffline()
    {
        if (refreshTokenStore.Read() is null || settingsService.SignedInUser is not { } signedInUserJson) return false;

        SignedInUserRecord? record;
        try { record = JsonSerializer.Deserialize(signedInUserJson, ClientJsonSerializerContext.Default.SignedInUserRecord); }
        catch (JsonException) { return false; }

        if (record is null || !TryGetServerAddress(out var serverAddress) || record.ServerAddress != serverAddress.AbsoluteUri) return false;
        CurrentUser = record.User;
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Returns a valid access token, refreshing it first when it expires soon.
    /// </summary>
    /// <exception cref="SessionExpiredException">The session could not be renewed.</exception>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var accessToken = _accessToken;
        if (accessToken is not null && _accessTokenExpiresAt - s_accessTokenRenewalMargin > DateTimeOffset.UtcNow) return accessToken;
        if (!await RefreshAsync(accessToken, cancellationToken)) throw new SessionExpiredException();
        return _accessToken ?? throw new SessionExpiredException();
    }

    /// <summary>
    /// Refreshes the tokens after <paramref name="rejectedAccessToken"/> was rejected or expired. Does nothing when another caller already replaced that token.
    /// When the server rejects the refresh token, the user is signed out and <see cref="SessionEnded"/> is raised.
    /// </summary>
    /// <returns><see langword="true"/> when a valid access token is available.</returns>
    public async Task<bool> RefreshAsync(string? rejectedAccessToken, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessToken != rejectedAccessToken) return true;
            if (refreshTokenStore.Read() is { } refreshToken && await RefreshCoreAsync(refreshToken, cancellationToken)) return true;
        }
        finally { _refreshGate.Release(); }

        ClearSession();
        SessionEnded?.Invoke(this, EventArgs.Empty);
        return false;
    }

    /// <summary>
    /// Signs out on this device and ends the session on the server when it can be reached.
    /// </summary>
    public async Task SignOutAsync()
    {
        var refreshToken = refreshTokenStore.Read();
        ClearSession();
        if (refreshToken is null || !TryGetServerAddress(out var serverAddress)) return;

        try { await apiClient.LogoutAsync(serverAddress, refreshToken, CancellationToken.None); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { }
    }

    public void UpdateCurrentUser(UserResponse user)
    {
        CurrentUser = user;
        if (TryGetServerAddress(out var serverAddress)) settingsService.SignedInUser = JsonSerializer.Serialize(new SignedInUserRecord(serverAddress.AbsoluteUri, user), ClientJsonSerializerContext.Default.SignedInUserRecord);
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> RefreshCoreAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            ApplyAuthentication(await apiClient.RefreshAsync(ServerAddress, refreshToken, cancellationToken));
            return true;
        }
        catch (ApiException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        {
            ClearSession();
            return false;
        }
    }

    private void ApplyAuthentication(AuthenticationResponse authentication)
    {
        refreshTokenStore.Write(authentication.RefreshToken);
        _accessToken = authentication.AccessToken;
        _accessTokenExpiresAt = authentication.AccessTokenExpiresAt;
        UpdateCurrentUser(authentication.User);
    }

    private void ClearSession()
    {
        refreshTokenStore.Clear();
        settingsService.SignedInUser = null;
        _accessToken = null;
        _accessTokenExpiresAt = default;
        CurrentUser = null;
    }
}
