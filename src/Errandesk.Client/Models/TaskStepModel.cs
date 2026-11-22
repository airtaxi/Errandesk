using Errandesk.Contracts;

namespace Errandesk.Client.Models;

/// <summary>
/// A step of a task.
/// </summary>
public sealed partial class TaskStepModel(TaskStepResponse response) : ObservableObject
{
    public Guid Id { get; } = response.Id;

    [ObservableProperty]
    public partial string Title { get; set; } = response.Title;

    [ObservableProperty]
    public partial bool IsCompleted { get; set; } = response.IsCompleted;

    public override string ToString() => Title;

    public void Apply(TaskStepResponse response)
    {
        Title = response.Title;
        IsCompleted = response.IsCompleted;
    }
}
