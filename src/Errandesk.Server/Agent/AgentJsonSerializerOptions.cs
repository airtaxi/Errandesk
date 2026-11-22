using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Errandesk.Contracts;
using Microsoft.Extensions.AI;

namespace Errandesk.Server.Agent;

/// <summary>
/// JSON settings for tool arguments, tool results and stored chat messages: camelCase names, string enums, the contract metadata and the chat abstraction types.
/// </summary>
public static class AgentJsonSerializerOptions
{
    public static JsonSerializerOptions Default { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(ErrandeskJsonSerializerContext.Default, AIJsonUtilities.DefaultOptions.TypeInfoResolver, new DefaultJsonTypeInfoResolver()),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }
}
