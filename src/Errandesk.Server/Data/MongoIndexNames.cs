namespace Errandesk.Server.Data;

/// <summary>
/// Names of indexes whose violations the server distinguishes, and of indexes with a fixed name.
/// </summary>
public static class MongoIndexNames
{
    public const string NoteText = "notes_text";
    public const string UniqueUserName = "normalizedUserName_unique";
    public const string UniqueFirstUser = "isFirstUser_unique";
    public const string UniqueInviteCode = "code_unique";
    public const string UniqueRefreshTokenHash = "tokenHash_unique";
    public const string UniqueDefaultList = "ownerUserId_isDefault_unique";
    public const string UniqueConversationMessageSequence = "conversationId_sequence_unique";
    public const string DueReminders = "reminderAt_due";
    public const string IdempotencyKeyExpiry = "createdAt_ttl";
    public const string UnsentAttachmentExpiry = "unsentExpiresAt_ttl";
    public const string Tombstones = "deletedAt_updatedAt_tombstones";
    public const string UniqueExternalItem = "ownerUserId_externalSource_externalId_unique";
    public const string UniqueApiTokenHash = "apiTokenHash_unique";
    public const string UniqueBriefingConversation = "ownerUserId_isBriefing_unique";
}
