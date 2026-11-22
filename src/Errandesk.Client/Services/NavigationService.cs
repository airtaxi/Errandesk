using System.Diagnostics.CodeAnalysis;

namespace Errandesk.Client.Services;

/// <summary>
/// Navigates the main window's frame between the top-level pages. Each navigation replaces the history, because the top-level pages have no back navigation.
/// </summary>
public sealed class NavigationService
{
    private Frame? _frame;

    public void Initialize(Frame frame) => _frame = frame;

    public bool Navigate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TPage>(object? parameter = null) where TPage : Page
    {
        var frame = _frame ?? throw new InvalidOperationException("The navigation frame is not initialized.");
        var isNavigated = frame.Navigate(typeof(TPage), parameter);
        frame.BackStack.Clear();
        return isNavigated;
    }
}
