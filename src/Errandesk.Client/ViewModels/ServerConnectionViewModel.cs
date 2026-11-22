using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// Checks a server address against <c>api/server-info</c>, shows the result, saves the address on success and continues to sign-in.
/// </summary>
public sealed partial class ServerConnectionViewModel(ErrandeskApiClient apiClient, ApplicationSettingsService settingsService, LoadingOverlayService loadingOverlayService, AppNavigationManager appNavigationManager) : ObservableObject
{
    private readonly ErrandeskApiClient _apiClient = apiClient;
    private readonly AppNavigationManager _appNavigationManager = appNavigationManager;
    private readonly ApplicationSettingsService _settingsService = settingsService;
    private readonly LoadingOverlayService _loadingOverlayService = loadingOverlayService;

    [ObservableProperty]
    public partial string ServerAddressText { get; set; } = settingsService.ServerAddress is { } savedAddress && Uri.TryCreate(savedAddress, UriKind.Absolute, out var savedUri) ? ServerAddressHelper.ToDisplayText(savedUri) : string.Empty;

    [ObservableProperty]
    public partial bool IsInsecureAddressWarningVisible { get; set; } = ServerAddressHelper.IsUnencryptedRemoteAddress(settingsService.ServerAddress);

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsServerInfoVisible { get; set; }

    [ObservableProperty]
    public partial string ServerVersionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegistrationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AssistantText { get; set; } = string.Empty;

    partial void OnServerAddressTextChanged(string value)
    {
        IsInsecureAddressWarningVisible = ServerAddressHelper.IsUnencryptedRemoteAddress(value);
        IsServerInfoVisible = false;
        IsErrorVisible = false;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        IsErrorVisible = false;
        IsServerInfoVisible = false;

        if (!ServerAddressHelper.TryNormalize(ServerAddressText, out var serverAddress))
        {
            ShowError(LocalizedStrings.Get("ServerAddressInvalidError"));
            return;
        }

        _loadingOverlayService.Show(LocalizedStrings.Get("ConnectingMessage"));
        try
        {
            var serverInfo = await _apiClient.GetServerInfoAsync(serverAddress, CancellationToken.None);
            _settingsService.ServerAddress = serverAddress.AbsoluteUri;
            ServerAddressText = ServerAddressHelper.ToDisplayText(serverAddress);
            ShowServerInfo(serverInfo);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is { } statusCode) { ShowError(string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("ServerStatusError"), (int)statusCode)); }
        catch (HttpRequestException) { ShowError(LocalizedStrings.Get("ServerUnreachableError")); }
        catch (TaskCanceledException) { ShowError(LocalizedStrings.Get("ServerTimeoutError")); }
        catch (InvalidDataException) { ShowError(LocalizedStrings.Get("ServerNotErrandeskError")); }
        finally { _loadingOverlayService.Hide(); }
    }

    [RelayCommand]
    private void Continue() => _appNavigationManager.ShowSignIn();

    private void ShowServerInfo(ServerInfoResponse serverInfo)
    {
        ServerVersionText = serverInfo.ServerVersion;
        RegistrationText = LocalizedStrings.Get(GetRegistrationResourceName(serverInfo));
        AssistantText = LocalizedStrings.Get(serverInfo.IsAiAvailable ? "AssistantAvailableText" : "AssistantUnavailableText");
        IsServerInfoVisible = true;
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        IsErrorVisible = true;
    }

    private static string GetRegistrationResourceName(ServerInfoResponse serverInfo) => serverInfo switch
    {
        { IsRegistrationAvailable: false } => "RegistrationClosedText",
        { RegistrationMode: RegistrationMode.Open } => "RegistrationOpenText",
        { RegistrationMode: RegistrationMode.Invite } => "RegistrationInviteText",
        _ => "RegistrationFirstAccountText",
    };
}
