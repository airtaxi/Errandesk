using System.Text;
using Errandesk.Contracts;
using Errandesk.Server.Data;

namespace Errandesk.Server.Services;

/// <summary>
/// Converts stored documents into contract responses.
/// </summary>
public static class ResponseMappings
{
    public const int ExcerptLength = 160;

    public static UserResponse ToResponse(this UserDocument user) => new(user.Id, user.UserName, user.DisplayName, user.IsAdministrator, user.TimeZone, user.Language, ToUtcOffset(user.CreatedAt), ToUtcOffset(user.UpdatedAt), user.ChangeVersion, user.MorningBriefing is { } briefing ? new MorningBriefingSettings(briefing.IsEnabled, briefing.Time) : MorningBriefingSettings.Default);

    public static InviteResponse ToResponse(this InviteDocument invite) => new(invite.Id, invite.Code, invite.OwnerUserId, ToUtcOffset(invite.CreatedAt), ToUtcOffset(invite.ExpiresAt), ToUtcOffset(invite.UsedAt), invite.UsedByUserId);

    /// <summary>Maps a list with its owner and members, taking their names from <paramref name="users"/>.</summary>
    public static TaskListResponse ToResponse(this TaskListDocument list, IReadOnlyDictionary<Guid, UserDocument> users) => new(list.Id, list.Name, list.SortOrder, list.IsDefault, ToUtcOffset(list.CreatedAt), ToUtcOffset(list.UpdatedAt), list.ChangeVersion, ToMemberResponse(list.OwnerUserId, list.CreatedAt, users), [.. list.Members.Select(member => ToMemberResponse(member.UserId, member.AddedAt, users))]);

    /// <summary>Maps a task for <paramref name="userId"/> with that user's linked note summaries and My Day date, computing whether it is in My Day for the user's <paramref name="today"/>.</summary>
    public static TaskItemResponse ToResponse(this TaskDocument task, Guid userId, DateOnly today, IReadOnlyList<NoteSummaryResponse> linkedNotes)
    {
        var myDayDate = task.GetMyDayDate(userId);
        return new TaskItemResponse(task.Id, task.ListId, task.Title, task.Body, task.IsImportant, task.IsCompleted, ToUtcOffset(task.CompletedAt), task.DueDate, myDayDate, myDayDate == today, task.SortOrder, [.. task.Steps.OrderBy(step => step.SortOrder).Select(step => new TaskStepResponse(step.Id, step.Title, step.IsCompleted, step.SortOrder))], ToUtcOffset(task.CreatedAt), ToUtcOffset(task.UpdatedAt), task.ChangeVersion, linkedNotes, ToUtcOffset(task.ReminderAt), task.Recurrence?.ToResponse(), task.NextOccurrenceId, task.ReminderAt is null ? null : task.ReminderSetByUserId);
    }

    public static DeviceResponse ToResponse(this DeviceDocument device) => new(device.Id, device.Platform, device.DeviceName, device.AppVersion, device.PushToken is not null, ToUtcOffset(device.LastSeenAt), ToUtcOffset(device.CreatedAt));

    /// <summary>Maps a note, listing only the linked task IDs contained in <paramref name="visibleTaskIds"/>.</summary>
    public static NoteResponse ToResponse(this NoteDocument note, IReadOnlySet<Guid> visibleTaskIds) => new(note.Id, note.Title, note.Content, [.. note.Tags], [.. note.LinkedTaskIds.Where(visibleTaskIds.Contains)], note.IsPinned, ToUtcOffset(note.CreatedAt), ToUtcOffset(note.UpdatedAt), note.ChangeVersion);

    public static AgentActionResponse ToResponse(this AgentActionDocument action) => new(action.Id, action.ConversationId, action.ToolName, action.EntityType, action.EntityId, ToUtcOffset(action.CreatedAt), ToUtcOffset(action.UndoneAt));

    public static ImportJobResponse ToResponse(this ImportJobDocument job) => new(job.Id, job.Source, job.Status, job.UserCode, job.VerificationUri, ToUtcOffset(job.ExpiresAt), job.ListsImported, job.ListsSkipped, job.TasksImported, job.TasksSkipped, job.StepsImported, job.Error, ToUtcOffset(job.CreatedAt), ToUtcOffset(job.CompletedAt));

    public static ConversationAttachmentResponse ToResponse(this ConversationAttachmentDocument attachment) => new(attachment.Id, attachment.MediaType, attachment.Data.LongLength, ToUtcOffset(attachment.CreatedAt));

    public static ConversationResponse ToResponse(this ConversationDocument conversation, bool hasPendingApprovals) => new(conversation.Id, conversation.Title, hasPendingApprovals, ToUtcOffset(conversation.CreatedAt), ToUtcOffset(conversation.UpdatedAt), conversation.IsBriefing);

    public static ApprovalRequestResponse ToResponse(this ApprovalRecordDocument approval) => new(approval.ApprovalId, approval.ToolName, approval.EntityType, approval.EntityId, approval.EntityTitle, approval.Status);

    public static NoteSummaryResponse ToSummaryResponse(this NoteDocument note) => new(note.Id, note.Title, CreateExcerpt(note.Content), [.. note.Tags], note.IsPinned, ToUtcOffset(note.UpdatedAt));

    /// <summary>The first <see cref="ExcerptLength"/> characters of <paramref name="content"/> after collapsing every run of white space into one space and trimming.</summary>
    public static string CreateExcerpt(string content)
    {
        var excerptBuilder = new StringBuilder(ExcerptLength);
        var isPendingSpace = false;
        foreach (var character in content)
        {
            if (char.IsWhiteSpace(character))
            {
                isPendingSpace = excerptBuilder.Length > 0;
                continue;
            }

            if (isPendingSpace) excerptBuilder.Append(' ');
            isPendingSpace = false;
            excerptBuilder.Append(character);
            if (excerptBuilder.Length >= ExcerptLength) break;
        }
        return excerptBuilder.ToString();
    }

    public static DateTimeOffset ToUtcOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static TaskListMemberResponse ToMemberResponse(Guid userId, DateTime addedAt, IReadOnlyDictionary<Guid, UserDocument> users)
    {
        var user = users.GetValueOrDefault(userId);
        return new TaskListMemberResponse(userId, user?.UserName ?? "", user?.DisplayName ?? "", ToUtcOffset(addedAt));
    }

    public static DateTimeOffset? ToUtcOffset(DateTime? value) => value is { } dateTime ? ToUtcOffset(dateTime) : null;
}
