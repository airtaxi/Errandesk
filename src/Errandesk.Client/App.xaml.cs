using Errandesk.Client.Managers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Client.ViewModels;
using Errandesk.Client.Views;
using Microsoft.UI.Dispatching;
using Uno.Resizetizer;

namespace Errandesk.Client;

public partial class App : Application
{
    private static readonly TimeSpan s_httpRequestTimeout = TimeSpan.FromSeconds(15);
    private static IServiceProvider? s_services;
    private static DispatcherQueue? s_dispatcherQueue;
    private static TaskNavigationRequest? s_pendingTaskNavigationRequest;

    private MainWindow? _mainWindow;

    public App() => InitializeComponent();

    /// <summary>
    /// Resolves a service registered at startup.
    /// </summary>
    public static TService GetService<TService>() where TService : notnull => s_services is null ? throw new InvalidOperationException("The application services are not configured yet.") : s_services.GetRequiredService<TService>();

    /// <summary>
    /// Brings the window to the front and shows a task whose notification the user tapped. Can be called from any thread,
    /// also before the app has launched; the task opens once the signed-in shell is shown.
    /// </summary>
    public static void OpenTaskFromNotification(TaskNavigationRequest request)
    {
        var dispatcherQueue = s_dispatcherQueue;
        if (s_services is null || dispatcherQueue is null)
        {
            s_pendingTaskNavigationRequest = request;
            return;
        }

        void OpenTask()
        {
            (Current as App)?._mainWindow?.Activate();
            GetService<TaskNavigationManager>().OpenTask(request);
        }

        if (dispatcherQueue.HasThreadAccess) OpenTask();
        else dispatcherQueue.TryEnqueue(OpenTask);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        s_services ??= ConfigureServices();

        if (_mainWindow is null)
        {
            var appearanceService = GetService<AppearanceService>();
            appearanceService.ApplySavedLanguage();

            var navigationService = GetService<NavigationService>();
            _mainWindow = new MainWindow(navigationService, GetService<BackNavigationManager>(), GetService<SyncManager>());
#if DEBUG
            _mainWindow.UseStudio();
#endif
            _mainWindow.SetWindowIcon();
            _mainWindow.VisibilityChanged += OnMainWindowVisibilityChanged;
#if ANDROID || IOS
            Windows.ApplicationModel.Core.CoreApplication.EnteredBackground += OnCoreApplicationEnteredBackground;
            Windows.ApplicationModel.Core.CoreApplication.LeavingBackground += OnCoreApplicationLeavingBackground;
#endif
            appearanceService.ApplySavedTheme();
            s_dispatcherQueue = _mainWindow.DispatcherQueue;
            if (s_pendingTaskNavigationRequest is { } pendingRequest)
            {
                s_pendingTaskNavigationRequest = null;
                GetService<TaskNavigationManager>().OpenTask(pendingRequest);
            }

#if WINDOWS
            WindowsNotificationActivation.Register();
#endif
            _mainWindow.Activate();
            _ = GetService<AppNavigationManager>().StartAsync();
            return;
        }

        _mainWindow.Activate();
    }

    /// <summary>
    /// Listens for data changes only while the window is visible.
    /// </summary>
    private static void OnMainWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args) => GetService<SyncManager>().SetForeground(args.Visible);

#if ANDROID || IOS
    private static void OnCoreApplicationEnteredBackground(object? _, Windows.ApplicationModel.EnteredBackgroundEventArgs __) => GetService<SyncManager>().SetForeground(false);

    private static void OnCoreApplicationLeavingBackground(object? _, Windows.ApplicationModel.LeavingBackgroundEventArgs __) => GetService<SyncManager>().SetForeground(true);
#endif

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ApplicationSettingsService>();
        services.AddSingleton<AppearanceService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<BackNavigationManager>();
        services.AddSingleton<LoadingOverlayService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<RefreshTokenStore>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<TaskListsManager>();
        services.AddSingleton<AppNavigationManager>();
        services.AddSingleton<NoteNavigationManager>();
        services.AddSingleton<TaskNavigationManager>();
        services.AddSingleton<SyncManager>();
        services.AddSingleton<ReminderManager>();
        services.AddSingleton<MicrosoftTodoImportManager>();
        services.AddSingleton<MorningBriefingManager>();
        services.AddSingleton<AssistantNavigationManager>();
#if WINDOWS
        services.AddSingleton<IReminderScheduler, WindowsReminderScheduler>();
#elif ANDROID
        services.AddSingleton<IReminderScheduler, AndroidReminderScheduler>();
#elif IOS
        services.AddSingleton<IReminderScheduler, AppleReminderScheduler>();
#else
        services.AddSingleton<IReminderScheduler, DesktopReminderScheduler>();
#endif
        services.AddTransient<AuthenticationHandler>();
        services.AddHttpClient<ErrandeskApiClient>(httpClient => httpClient.Timeout = s_httpRequestTimeout);
        services.AddHttpClient<AuthenticatedApiClient>(httpClient => httpClient.Timeout = s_httpRequestTimeout).AddHttpMessageHandler<AuthenticationHandler>();
        services.AddHttpClient<ServerEventStreamClient>(httpClient => httpClient.Timeout = Timeout.InfiniteTimeSpan).AddHttpMessageHandler<AuthenticationHandler>();
        services.AddHttpClient(SyncManager.HttpClientName, httpClient => httpClient.Timeout = s_httpRequestTimeout).AddHttpMessageHandler<AuthenticationHandler>();
        services.AddTransient<ServerConnectionViewModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<SignUpViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<TaskListViewModel>();
        services.AddTransient<TaskDetailViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<NotesViewModel>();
        services.AddTransient<NoteEditorViewModel>();
        services.AddTransient<AssistantViewModel>();
        services.AddTransient<ShareListViewModel>();
        return services.BuildServiceProvider();
    }
}
