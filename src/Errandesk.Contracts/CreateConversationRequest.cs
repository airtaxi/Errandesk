namespace Errandesk.Contracts;

/// <summary>
/// Creates an assistant conversation.
/// </summary>
/// <param name="Title">Optional title. When empty, the first message sets it.</param>
/// <param name="Id">Client-generated ID. Creating an ID that already exists for the same user returns the existing conversation.</param>
public sealed record CreateConversationRequest(string? Title = null, Guid? Id = null);
