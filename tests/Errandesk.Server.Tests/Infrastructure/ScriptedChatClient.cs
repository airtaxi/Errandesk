using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// A chat model stand-in that answers each request with the next scripted response and records every request it receives.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Lock _gate = new();
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, IEnumerable<ChatResponseUpdate>>> _responses = new();
    private readonly List<IReadOnlyList<ChatMessage>> _receivedMessages = [];
    private readonly List<ChatOptions?> _receivedOptions = [];
    private int _responseCount;

    /// <summary>The messages of every request, oldest request first.</summary>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> ReceivedMessages
    {
        get
        {
            lock (_gate)
            {
                return [.. _receivedMessages];
            }
        }
    }

    /// <summary>The options of every request, oldest request first.</summary>
    public IReadOnlyList<ChatOptions?> ReceivedOptions
    {
        get
        {
            lock (_gate)
            {
                return [.. _receivedOptions];
            }
        }
    }

    public int PendingResponseCount
    {
        get
        {
            lock (_gate)
            {
                return _responses.Count;
            }
        }
    }

    /// <summary>Adds a response computed from the request messages. It may throw to simulate a failing service.</summary>
    public void Enqueue(Func<IReadOnlyList<ChatMessage>, IEnumerable<ChatResponseUpdate>> response)
    {
        lock (_gate)
        {
            _responses.Enqueue(response);
        }
    }

    /// <summary>Adds a response that streams the text in the given pieces.</summary>
    public void EnqueueText(params string[] pieces) => Enqueue(_ => [.. pieces.Select(piece => new ChatResponseUpdate(ChatRole.Assistant, piece))]);

    /// <summary>Adds a response that optionally streams text and then calls one tool with arguments given as a JSON object.</summary>
    public void EnqueueToolCall(string callId, string toolName, string argumentsJson, string? text = null) => Enqueue(_ => CreateToolCallUpdates(callId, toolName, argumentsJson, text));

    public static IEnumerable<ChatResponseUpdate> CreateToolCallUpdates(string callId, string toolName, string argumentsJson, string? text = null)
    {
        if (text is not null) yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson, AIJsonUtilities.DefaultOptions);
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(callId, toolName, arguments)]);
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages.Select(message => message.Clone()).ToList();
        Func<IReadOnlyList<ChatMessage>, IEnumerable<ChatResponseUpdate>> response;
        string messageId;
        lock (_gate)
        {
            _receivedMessages.Add(messageList);
            _receivedOptions.Add(options);
            if (!_responses.TryDequeue(out response!)) throw new InvalidOperationException("No scripted response is left.");
            messageId = $"response-{++_responseCount}";
        }

        await Task.Yield();
        foreach (var update in response(messageList))
        {
            cancellationToken.ThrowIfCancellationRequested();
            update.MessageId ??= messageId;
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
