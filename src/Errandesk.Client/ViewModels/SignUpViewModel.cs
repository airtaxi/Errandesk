using System.Net;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Services;
using Errandesk.Contracts;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// Creates an account on the saved server and signs it in.
/// </summary>
public sealed partial class SignUpViewModel(SessionManager sessionManager, ErrandeskApiClient apiClient, ApplicationSettingsService settingsService, AppNavigationManager appNavigationManager, LoadingOverlayService loadingOverlayService) : ObservableObject
{
    private const int MinimumPasswordLength = 8;

    private readonly SessionManager _sessionManager = sessionManager;
    private readonly ErrandeskApiClient _apiClient = apiClient;
    private readonly ApplicationSettingsService _settingsService = settingsService;
    private readonly AppNavigationManager _appNavigationManager = appNavigationManager;
    private readonly LoadingOverlayService _loadingOverlayService = loadingOverlayService;

    [ObservableProperty]
    public partial string ServerAddressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InviteCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsInviteCodeVisible { get; set; }

    [ObservableProperty]
    public partial bool IsRegistrationClosed { get; set; }

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!_sessionManager.TryGetServerAddress(out var serverAddress)) return;
        ServerAddressText = ServerAddressHelper.ToDisplayText(serverAddress);

        try
        {
            var serverInfo = await _apiClient.GetServerInfoAsync(serverAddress, CancellationToken.None);
            IsInviteCodeVisible = serverInfo.RegistrationMode == RegistrationMode.Invite;
            IsRegistrationClosed = !serverInfo.IsRegistrationAvailable;
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(ApiErrorMessages.GetMessage(exception)); }
    }

    [RelayCommand]
    private async Task SignUpAsync()
    {
        IsErrorVisible = false;
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Password))
        {
            ShowError(LocalizedStrings.Get("SignInMissingFieldsError"));
            return;
        }

        if (Password.Length < MinimumPasswordLength)
        {
            ShowError(LocalizedStrings.Get("PasswordTooShortError"));
            return;
        }

        if (Password != ConfirmPassword)
        {
            ShowError(LocalizedStrings.Get("PasswordMismatchError"));
            return;
        }

        var request = new RegisterRequest(UserName.Trim(), Password, DeviceInformationHelper.GetIanaTimeZoneId(), AppearanceService.GetUserLanguage(_settingsService.LanguagePreference), string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(), string.IsNullOrWhiteSpace(InviteCode) ? null : InviteCode.Trim(), DeviceInformationHelper.GetDeviceName());
        _loadingOverlayService.Show(LocalizedStrings.Get("CreatingAccountMessage"));
        try
        {
            await _sessionManager.RegisterAsync(request, CancellationToken.None);
            Password = ConfirmPassword = string.Empty;
            _appNavigationManager.ShowShell();
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict) { ShowError(LocalizedStrings.Get("UserNameTakenError")); }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden) { ShowError(LocalizedStrings.Get("RegistrationClosedError")); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(ApiErrorMessages.GetMessage(exception)); }
        finally { _loadingOverlayService.Hide(); }
    }

    [RelayCommand]
    private void ShowSignIn() => _appNavigationManager.ShowSignIn();

    private void ShowError(string? message)
    {
        if (message is null) return;
        ErrorMessage = message;
        IsErrorVisible = true;
    }
}
