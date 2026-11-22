using Errandesk.Server.Agent;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace Errandesk.Server.Mcp;

/// <summary>
/// Runs an assistant tool for an MCP call: makes the call's arguments available for the agent action record,
/// and turns an error the tool reported into a tool result marked as an error.
/// </summary>
public sealed class McpAgentToolFunction(AIFunction innerFunction) : DelegatingAIFunction(innerFunction)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var invocation = AgentToolInvocation.Begin(arguments);
        var result = await base.InvokeCoreAsync(arguments, cancellationToken);
        return invocation.Error is { } error ? new CallToolResult { Content = [new TextContentBlock { Text = error }], IsError = true } : result;
    }
}
