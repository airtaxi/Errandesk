namespace Errandesk.Contracts;

/// <summary>
/// The format of personal access tokens and the path of the MCP endpoint they unlock.
/// </summary>
public static class PersonalAccessTokenFormat
{
    /// <summary>Every personal access token starts with this text.</summary>
    public const string Prefix = "edk_";

    /// <summary>Path of the MCP endpoint relative to the server address.</summary>
    public const string McpEndpointPath = "mcp";
}
