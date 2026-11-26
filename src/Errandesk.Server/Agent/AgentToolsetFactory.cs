using Errandesk.Server.Services;
using Errandesk.Server.Web;

namespace Errandesk.Server.Agent;

/// <summary>
/// Creates the toolset of an assistant run from the shared domain services. The web page reader is registered only when reading web pages is enabled.
/// </summary>
public sealed class AgentToolsetFactory(TaskListService taskListService, TaskService taskService, NoteService noteService, UserService userService, AgentActionService agentActionService, TimeProvider timeProvider, WebPageReader? webPageReader = null)
{
    public AgentToolset Create(AgentRunContext context) => new(taskListService, taskService, noteService, userService, agentActionService, timeProvider, context, webPageReader);
}
