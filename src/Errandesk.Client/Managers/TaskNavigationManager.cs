using Errandesk.Client.Models;

namespace Errandesk.Client.Managers;

/// <summary>
/// Lets notifications open a task in the shell. A request made before the shell is shown waits until the shell takes it.
/// </summary>
/// <remarks>Call every member on the UI thread.</remarks>
public sealed class TaskNavigationManager
{
    private TaskNavigationRequest? _pendingRequest;

    /// <summary>
    /// Raised when a request is waiting. The shell handles it with <see cref="TryTakePendingRequest"/>.
    /// </summary>
    public event EventHandler? TaskOpenRequested;

    public void OpenTask(TaskNavigationRequest request)
    {
        _pendingRequest = request;
        TaskOpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public bool TryTakePendingRequest(out TaskNavigationRequest request)
    {
        request = _pendingRequest!;
        _pendingRequest = null;
        return request is not null;
    }

    /// <summary>
    /// Drops a waiting request, for example when the user signs out.
    /// </summary>
    public void Clear() => _pendingRequest = null;
}
