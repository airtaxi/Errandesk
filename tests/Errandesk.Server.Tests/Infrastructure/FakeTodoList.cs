namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// A Microsoft To Do list served by <see cref="FakeMicrosoftTodo"/>.
/// </summary>
/// <param name="WellknownListName"><c>none</c>, <c>defaultList</c> or <c>flaggedEmails</c>.</param>
public sealed record FakeTodoList(string Id, string DisplayName, string WellknownListName = "none")
{
    public List<FakeTodoTask> Tasks { get; } = [];
}
