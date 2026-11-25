using Errandesk.Client.Models;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Picks the template of a row in a task list: a task, or the header of the completed section.
/// </summary>
public sealed partial class TaskRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TaskTemplate { get; set; }

    public DataTemplate? CompletedSectionTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is CompletedSectionModel ? CompletedSectionTemplate : TaskTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
