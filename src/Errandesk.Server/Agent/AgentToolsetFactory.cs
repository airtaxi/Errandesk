using Errandesk.Server.Services;

namespace Errandesk.Server.Agent;

/// <summary>
/// Creates the toolset of an assistant run from the shared domain services.
/// </summary>
public sealed class AgentToolsetFactory(TaskListService taskListService, TaskService taskService, NoteService noteService, UserService userService, AgentActionService agentActionService, TimeProvider timeProvider)
{
    public AgentToolset Create(AgentRunContext context) => new(taskListService, taskService, noteService, userService, agentActionService, timeProvider, context);
}
