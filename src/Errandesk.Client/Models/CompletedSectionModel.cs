namespace Errandesk.Client.Models;

/// <summary>
/// The header row of the completed section in a task list, which shows the number of completed tasks and expands or collapses them.
/// </summary>
public sealed partial class CompletedSectionModel : ObservableObject
{
    [ObservableProperty]
    public partial string HeaderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}
