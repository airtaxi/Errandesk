using Errandesk.Contracts;
using Errandesk.Server.Data;
using MongoDB.Driver;

namespace Errandesk.Server.Services;

/// <summary>
/// Stores the images users attach to assistant messages. An image is uploaded first, then sent with a message, which keeps it as long as the conversation.
/// </summary>
public sealed class ConversationAttachmentService(ErrandeskDatabase database, ConversationService conversationService, ChangeVersionGenerator changeVersionGenerator, DocumentUpdater documentUpdater)
{
    /// <summary>How long an uploaded image waits to be sent before it is removed.</summary>
    public static readonly TimeSpan UnsentRetention = TimeSpan.FromDays(1);

    private const string ResourceName = "attachment";

    /// <summary>Stores the image read from <paramref name="content"/> for the next message of the conversation.</summary>
    public async Task<ConversationAttachmentResponse> CreateAsync(Guid userId, Guid conversationId, Stream content, CancellationToken cancellationToken)
    {
        await conversationService.GetVisibleAsync(userId, conversationId, cancellationToken);
        var data = await ReadImageAsync(content, cancellationToken);
        var mediaType = ImageMediaTypes.Detect(data) ?? throw new RequestValidationException("The attachment must be a JPEG, PNG, GIF or WebP image.");

        var utcNow = documentUpdater.GetUtcNow();
        var attachment = new ConversationAttachmentDocument { Id = Guid.CreateVersion7(), ConversationId = conversationId, MediaType = mediaType, Data = data, UnsentExpiresAt = utcNow + UnsentRetention };
        attachment.StampCreated(userId, utcNow, await changeVersionGenerator.NextAsync(cancellationToken));
        await database.ConversationAttachments.InsertOneAsync(attachment, cancellationToken: cancellationToken);
        return attachment.ToResponse();
    }

    public async Task<ConversationAttachmentDocument> GetAsync(Guid userId, Guid conversationId, Guid attachmentId, CancellationToken cancellationToken) => await database.ConversationAttachments.Find(attachment => attachment.Id == attachmentId && attachment.ConversationId == conversationId && attachment.OwnerUserId == userId).FirstOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException(ResourceName);

    /// <summary>
    /// The unsent images of the conversation with the given IDs, in the given order. Throws when there are too many, an ID repeats, or an image is missing or already sent.
    /// </summary>
    public async Task<List<ConversationAttachmentDocument>> GetUnsentAsync(Guid userId, Guid conversationId, IReadOnlyList<Guid>? attachmentIds, CancellationToken cancellationToken)
    {
        if (attachmentIds is not { Count: > 0 }) return [];
        if (attachmentIds.Count > ConversationAttachmentLimits.MaximumCount) throw new RequestValidationException($"A message can carry at most {ConversationAttachmentLimits.MaximumCount} images.");
        if (attachmentIds.Distinct().Count() != attachmentIds.Count) throw new RequestValidationException("Each image can be attached only once.");

        await conversationService.GetVisibleAsync(userId, conversationId, cancellationToken);
        var filterBuilder = Builders<ConversationAttachmentDocument>.Filter;
        var filter = filterBuilder.In(attachment => attachment.Id, attachmentIds) & filterBuilder.Eq(attachment => attachment.ConversationId, conversationId) & filterBuilder.Eq(attachment => attachment.OwnerUserId, userId) & filterBuilder.Eq(attachment => attachment.MessageId, null);
        var attachments = (await database.ConversationAttachments.Find(filter).ToListAsync(cancellationToken)).ToDictionary(attachment => attachment.Id);
        if (attachments.Count != attachmentIds.Count) throw new RequestValidationException("An attached image was not found or was already sent. Upload it again.");
        return [.. attachmentIds.Select(attachmentId => attachments[attachmentId])];
    }

    /// <summary>Assigns unsent images to the user message that carries them, which stops them from expiring.</summary>
    public async Task MarkSentAsync(IReadOnlyList<Guid> attachmentIds, Guid messageId, CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<ConversationAttachmentDocument>.Filter;
        var filter = filterBuilder.In(attachment => attachment.Id, attachmentIds) & filterBuilder.Eq(attachment => attachment.MessageId, null);
        var update = Builders<ConversationAttachmentDocument>.Update.Set(attachment => attachment.MessageId, messageId).Unset(attachment => attachment.UnsentExpiresAt).Set(attachment => attachment.UpdatedAt, documentUpdater.GetUtcNow());
        await database.ConversationAttachments.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
    }

    /// <summary>The images of the conversation with the given IDs, by ID. Images that no longer exist are left out.</summary>
    public async Task<Dictionary<Guid, ConversationAttachmentDocument>> GetManyAsync(Guid conversationId, IReadOnlyCollection<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        if (attachmentIds.Count == 0) return [];
        var filterBuilder = Builders<ConversationAttachmentDocument>.Filter;
        var attachments = await database.ConversationAttachments.Find(filterBuilder.In(attachment => attachment.Id, attachmentIds) & filterBuilder.Eq(attachment => attachment.ConversationId, conversationId)).ToListAsync(cancellationToken);
        return attachments.ToDictionary(attachment => attachment.Id);
    }

    private static async Task<byte[]> ReadImageAsync(Stream content, CancellationToken cancellationToken)
    {
        var maximumLengthText = $"{ConversationAttachmentLimits.MaximumLength / (1024 * 1024)} MB";
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int readCount;
        while ((readCount = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + readCount > ConversationAttachmentLimits.MaximumLength) throw new RequestValidationException($"An image must be at most {maximumLengthText}.");
            buffer.Write(chunk, 0, readCount);
        }

        if (buffer.Length == 0) throw new RequestValidationException("The image must not be empty.");
        return buffer.ToArray();
    }
}
