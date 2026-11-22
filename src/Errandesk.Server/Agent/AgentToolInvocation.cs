namespace Errandesk.Server.Agent;

/// <summary>
/// A tool call made outside an assistant run, such as through the MCP endpoint: its arguments, and the error the tool reported.
/// The current invocation flows with the asynchronous call into the tool.
/// </summary>
public sealed class AgentToolInvocation(IDictionary<string, object?> arguments)
{
    private static readonly AsyncLocal<AgentToolInvocation?> s_current = new();

    /// <summary>The invocation of the tool that is running on this asynchronous flow, if any.</summary>
    public static AgentToolInvocation? Current => s_current.Value;

    public IDictionary<string, object?> Arguments => arguments;

    /// <summary>The error the tool returned to the caller, or <see langword="null"/> when it succeeded.</summary>
    public string? Error { get; set; }

    /// <summary>Makes a new invocation current for the rest of the calling asynchronous method and the calls it awaits.</summary>
    public static AgentToolInvocation Begin(IDictionary<string, object?> arguments)
    {
        var invocation = new AgentToolInvocation(arguments);
        s_current.Value = invocation;
        return invocation;
    }
}
