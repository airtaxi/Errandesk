using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Speech;
using Android.Views;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace Errandesk.Client.Droid;

[Activity(MainLauncher = true, ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges, WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden, Exported = true)]
public class MainActivity : Uno.UI.Runtime.Android.ApplicationActivity
{
    /// <summary>The intent action that opens the assistant pane, which the home screen widget sends.</summary>
    public const string OpenAssistantAction = "errandesk.OPEN_ASSISTANT";

    private const int NotificationPermissionRequestCode = 1001;

    private static MainActivity? s_runningActivity;

    private ActivityResultLauncher? _speechRecognitionLauncher;
    private TaskCompletionSource<string?>? _speechRecognitionCompletion;

    /// <summary>
    /// Whether the device has an app that handles <see cref="RecognizerIntent.ActionRecognizeSpeech"/>.
    /// </summary>
    public static bool IsSpeechRecognitionAvailable => Android.App.Application.Context.PackageManager is { } packageManager && new Intent(RecognizerIntent.ActionRecognizeSpeech).ResolveActivity(packageManager) is not null;

    /// <summary>
    /// Asks for permission to post notifications, which Android 13 and later require. Does nothing when it is granted already.
    /// </summary>
    public static void RequestNotificationPermission()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || s_runningActivity is not { } activity) return;
        if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.PostNotifications) == Permission.Granted) return;

        ActivityCompat.RequestPermissions(activity, [Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
    }

    /// <summary>
    /// Opens the speech recognition screen of the system and returns the most likely transcription.
    /// A request that is still waiting completes with <see langword="null"/> when a new one starts.
    /// </summary>
    /// <param name="prompt">The text the speech recognition screen shows while it listens.</param>
    /// <returns>The recognized text, or <see langword="null"/> when the user cancels, nothing is recognized, or no app handles speech recognition.</returns>
    public static Task<string?> RecognizeSpeechAsync(string prompt)
    {
        if (s_runningActivity is not { _speechRecognitionLauncher: { } launcher } activity) return Task.FromResult<string?>(null);

        activity._speechRecognitionCompletion?.TrySetResult(null);
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity._speechRecognitionCompletion = completion;

        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraPrompt, prompt);
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
        try { launcher.Launch(intent); }
        catch (ActivityNotFoundException)
        {
            activity._speechRecognitionCompletion = null;
            return Task.FromResult<string?>(null);
        }

        return completion.Task;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
        s_runningActivity = this;
        _speechRecognitionLauncher = RegisterForActivityResult(new ActivityResultContracts.StartActivityForResult(), new ActivityResultHandler(OnSpeechRecognitionResult));
        HandleLaunchIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleLaunchIntent(intent);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(s_runningActivity, this)) s_runningActivity = null;
        _speechRecognitionCompletion?.TrySetResult(null);
        _speechRecognitionCompletion = null;
        base.OnDestroy();
    }

    private void OnSpeechRecognitionResult(ActivityResult result)
    {
        var completion = _speechRecognitionCompletion;
        _speechRecognitionCompletion = null;
        var text = result.ResultCode == (int)Result.Ok ? result.Data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults)?.FirstOrDefault() : null;
        completion?.TrySetResult(text);
    }

    /// <summary>
    /// Opens the assistant pane or the task an intent asks for, then removes the request from the intent so it is handled only once.
    /// </summary>
    private static void HandleLaunchIntent(Intent? intent)
    {
        if (intent?.Action == OpenAssistantAction)
        {
            intent.SetAction(null);
            App.OpenAssistant();
            return;
        }

        OpenNotificationTask(intent);
    }

    /// <summary>
    /// Opens the task of a tapped reminder notification, then removes the task from the intent so it opens only once.
    /// </summary>
    private static void OpenNotificationTask(Intent? intent)
    {
        if (!ReminderNotifications.TryReadTarget(intent, out var request)) return;

        intent!.RemoveExtra(ReminderNotifications.TaskIdExtra);
        intent.RemoveExtra(ReminderNotifications.ListIdExtra);
        App.OpenTaskFromNotification(request);
    }
}
