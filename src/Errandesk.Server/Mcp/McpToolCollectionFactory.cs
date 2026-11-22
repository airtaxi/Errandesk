using Errandesk.Contracts;
using Errandesk.Server.Agent;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Errandesk.Server.Mcp;

/// <summary>
/// Builds the MCP tools of one user from the assistant toolset, with annotations that mark read-only and destructive tools.
/// </summary>
public sealed class McpToolCollectionFactory(AgentToolsetFactory toolsetFactory)
{
    public McpServerPrimitiveCollection<McpServerTool> Create(Guid userId)
    {
        var context = new AgentRunContext(userId, null) { ToolCompleted = RecordToolErrorAsync };
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var function in toolsetFactory.Create(context).CreateTools().OfType<AIFunction>()) tools.Add(McpServerTool.Create(new McpAgentToolFunction(function), CreateOptions(function.Name)));
        return tools;
    }

    private static McpServerToolCreateOptions CreateOptions(string toolName) => new()
    {
        Name = toolName,
        ReadOnly = AgentToolset.IsReadOnly(toolName),
        Destructive = AgentToolset.RequiresApproval(toolName),
        OpenWorld = false,
    };

    private static Task RecordToolErrorAsync(ToolCompletedEventData data)
    {
        if (!data.Succeeded && AgentToolInvocation.Current is { } invocation) invocation.Error = data.Error;
        return Task.CompletedTask;
    }
}
