using System.Net;
using Errandesk.Client.Helpers;
using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;

namespace Errandesk.Client.ViewModels;

/// <summary>
/// Signs in to the saved server with a user name and password.
/// </summary>
public sealed partial class SignInViewModel(SessionManager sessionManager, ErrandeskApiClient apiClient, AppNavigationManager appNavigationManager, LoadingOverlayService loadingOverlayService) : ObservableObject
{
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly ErrandeskApiClient _apiClient = apiClient;
    private readonly AppNavigationManager _appNavigationManager = appNavigationManager;
    private readonly LoadingOverlayService _loadingOverlayService = loadingOverlayService;

    [ObservableProperty]
    public partial string ServerAddressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsErrorVisible { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRestoreRetryVisible { get; set; }

    [ObservableProperty]
    public partial bool IsSignUpAvailable { get; set; }

    public async Task InitializeAsync(SignInPageParameter? parameter)
    {
        ServerAddressText = _sessionManager.TryGetServerAddress(out var serverAddress) ? ServerAddressHelper.ToDisplayText(serverAddress) : string.Empty;
        IsRestoreRetryVisible = parameter?.IsRestoreRetryAvailable == true;
        if (parameter?.Message is { } message) ShowError(message);

        if (!_sessionManager.TryGetServerAddress(out serverAddress)) return;
        try { IsSignUpAvailable = (await _apiClient.GetServerInfoAsync(serverAddress, CancellationToken.None)).IsRegistrationAvailable; }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { IsSignUpAvailable = false; }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        IsErrorVisible = false;
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Password))
        {
            ShowError(LocalizedStrings.Get("SignInMissingFieldsError"));
            return;
        }

        _loadingOverlayService.Show(LocalizedStrings.Get("SigningInMessage"));
        try
        {
            await _sessionManager.SignInAsync(UserName.Trim(), Password, CancellationToken.None);
            Password = string.Empty;
            _appNavigationManager.ShowShell();
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized) { ShowError(LocalizedStrings.Get("SignInFailedError")); }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(ApiErrorMessages.GetMessage(exception)); }
        finally { _loadingOverlayService.Hide(); }
    }

    [RelayCommand]
    private async Task RetryRestoreAsync()
    {
        IsErrorVisible = false;
        _loadingOverlayService.Show(LocalizedStrings.Get("SigningInMessage"));
        try
        {
            if (await _sessionManager.TryRestoreAsync(CancellationToken.None)) _appNavigationManager.ShowShell();
            else IsRestoreRetryVisible = false;
        }
        catch (Exception exception) when (ApiErrorMessages.IsRequestFailure(exception)) { ShowError(ApiErrorMessages.GetMessage(exception)); }
        finally { _loadingOverlayService.Hide(); }
    }

    [RelayCommand]
    private void ShowSignUp() => _appNavigationManager.ShowSignUp();

    [RelayCommand]
    private Task ChangeServerAsync() => _appNavigationManager.ChangeServerAsync();

    private void ShowError(string? message)
    {
        if (message is null) return;
        ErrorMessage = message;
        IsErrorVisible = true;
    }
}
