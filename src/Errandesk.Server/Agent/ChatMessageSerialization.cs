using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Errandesk.Server.Agent;

/// <summary>
/// Converts chat message contents to and from the JSON stored with conversation messages, and repairs histories so every tool call has its result.
/// </summary>
public static class ChatMessageSerialization
{
    public static string SerializeContents(IList<AIContent> contents) => JsonSerializer.Serialize(contents, AIJsonUtilities.DefaultOptions);

    public static List<AIContent> DeserializeContents(string json) => JsonSerializer.Deserialize<List<AIContent>>(json, AIJsonUtilities.DefaultOptions) ?? [];

    /// <summary>The concatenated text contents of a message.</summary>
    public static string GetText(IEnumerable<AIContent> contents) => string.Concat(contents.OfType<TextContent>().Select(content => content.Text));

    /// <summary>
    /// Removes tool calls without a result and results without a call, then drops messages left without contents.
    /// Approval requests are kept: their tool calls run only after the user decides.
    /// </summary>
    public static List<ChatMessage> RemoveUnpairedToolContents(IEnumerable<ChatMessage> messages)
    {
        var messageList = messages.ToList();
        var callIds = messageList.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Select(call => call.CallId).ToHashSet(StringComparer.Ordinal);
        var resultIds = messageList.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.CallId).ToHashSet(StringComparer.Ordinal);

        List<ChatMessage> repairedMessages = [];
        foreach (var message in messageList)
        {
            var contents = message.Contents.Where(content => content switch
            {
                FunctionCallContent call => resultIds.Contains(call.CallId),
                FunctionResultContent result => callIds.Contains(result.CallId),
                _ => true,
            }).ToList();
            if (contents.Count == 0) continue;
            if (contents.Count == message.Contents.Count)
            {
                repairedMessages.Add(message);
                continue;
            }

            var repairedMessage = message.Clone();
            repairedMessage.Contents = contents;
            repairedMessages.Add(repairedMessage);
        }
        return repairedMessages;
    }
}
