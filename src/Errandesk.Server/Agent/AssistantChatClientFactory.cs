using System.ClientModel;
using Errandesk.Server.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Errandesk.Server.Agent;

/// <summary>
/// Creates the chat client for the configured OpenAI-compatible endpoint, using the Chat Completions API.
/// </summary>
public static class AssistantChatClientFactory
{
    /// <summary>Credential sent to custom endpoints that accept requests without an API key.</summary>
    public const string PlaceholderApiKey = "unused";

    public static IChatClient Create(AiSettings settings)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = settings.Endpoint };
        var client = new OpenAIClient(new ApiKeyCredential(settings.ApiKey ?? PlaceholderApiKey), clientOptions);
        return client.GetChatClient(settings.Model).AsIChatClient();
    }
}
