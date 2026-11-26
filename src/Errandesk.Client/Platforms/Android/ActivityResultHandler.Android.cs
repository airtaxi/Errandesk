using Android.Runtime;
using AndroidX.Activity.Result;

namespace Errandesk.Client.Droid;

/// <summary>
/// Passes the result of an activity started through an <see cref="ActivityResultLauncher"/> to a delegate.
/// </summary>
internal sealed class ActivityResultHandler(Action<ActivityResult> onResult) : Java.Lang.Object, IActivityResultCallback
{
    private readonly Action<ActivityResult> _onResult = onResult;

    public void OnActivityResult(Java.Lang.Object? result)
    {
        if (result?.JavaCast<ActivityResult>() is { } activityResult)
        {
            _onResult(activityResult);
        }
    }
}
