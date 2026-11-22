namespace Errandesk.Server.Data;

/// <summary>
/// A task with its embedded steps.
/// </summary>
public sealed class TaskDocument : DocumentBase
{
    public Guid ListId { get; set; }

    public string Title { get; set; } = "";

    /// <summary>Plain-text note, empty when there is none.</summary>
    public string Body { get; set; } = "";

    public bool IsImportant { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateOnly? DueDate { get; set; }

    /// <summary>The owner's My Day date. The task is in a user's My Day while their date equals today in their time zone.</summary>
    public DateOnly? MyDayDate { get; set; }

    /// <summary>My Day dates of members of a shared list, one per user. The owner's date is <see cref="MyDayDate"/>.</summary>
    public List<TaskMyDayDocument> MemberMyDayDates { get; set; } = [];

    /// <summary>Users who could see the task before it moved to a list they cannot see. The change feed tells them to drop it. A user who can see the task again is taken off this list.</summary>
    public List<Guid> RevokedUserIds { get; set; } = [];

    public double SortOrder { get; set; }

    public List<TaskStepDocument> Steps { get; set; } = [];

    /// <summary>When the reminder is due, in UTC.</summary>
    public DateTime? ReminderAt { get; set; }

    /// <summary>The user who set the reminder and receives its push notifications.</summary>
    public Guid? ReminderSetByUserId { get; set; }

    /// <summary>When the reminder was handled by the dispatcher; a handled reminder is not sent again.</summary>
    public DateTime? ReminderSentAt { get; set; }

    /// <summary>When a dispatcher claimed the reminder for sending.</summary>
    public DateTime? ReminderClaimedAt { get; set; }

    /// <summary>Identifies the claim, so only the claiming dispatcher marks the reminder as sent.</summary>
    public Guid? ReminderClaimId { get; set; }

    /// <summary>The repeat rule, or <see langword="null"/> when the task does not repeat.</summary>
    public TaskRecurrenceDocument? Recurrence { get; set; }

    /// <summary>ID of the occurrence created when this repeating task was completed. Set once, so completing again never creates another occurrence.</summary>
    public Guid? NextOccurrenceId { get; set; }

    /// <summary>Service the task was imported from, one of <see cref="Errandesk.Contracts.ImportSources"/>, or <see langword="null"/> for tasks created in Errandesk.</summary>
    public string? ExternalSource { get; set; }

    /// <summary>ID of the task in <see cref="ExternalSource"/>.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Sets or removes the reminder, attributing it to <paramref name="userId"/> and making it due for sending again.</summary>
    public void SetReminder(DateTime? reminderAt, Guid userId)
    {
        ReminderAt = reminderAt;
        ReminderSetByUserId = reminderAt is null ? null : userId;
        ReminderSentAt = null;
        ReminderClaimedAt = null;
        ReminderClaimId = null;
    }

    /// <summary>The My Day date of <paramref name="userId"/>, or <see langword="null"/> when the task is not in that user's My Day.</summary>
    public DateOnly? GetMyDayDate(Guid userId) => userId == OwnerUserId ? MyDayDate : MemberMyDayDates.Find(entry => entry.UserId == userId)?.Date;

    /// <summary>Sets or removes the My Day date of <paramref name="userId"/> without touching other users' My Day.</summary>
    public void SetMyDayDate(Guid userId, DateOnly? date)
    {
        if (userId == OwnerUserId)
        {
            MyDayDate = date;
            return;
        }

        MemberMyDayDates.RemoveAll(entry => entry.UserId == userId);
        if (date is { } myDayDate) MemberMyDayDates.Add(new TaskMyDayDocument { UserId = userId, Date = myDayDate });
    }

    /// <summary>Makes <paramref name="newOwnerUserId"/> the owner, keeping every user's My Day date.</summary>
    public void ChangeOwner(Guid newOwnerUserId)
    {
        if (newOwnerUserId == OwnerUserId) return;

        var previousOwnerUserId = OwnerUserId;
        var previousOwnerMyDayDate = MyDayDate;
        var newOwnerMyDayDate = GetMyDayDate(newOwnerUserId);
        MemberMyDayDates.RemoveAll(entry => entry.UserId == newOwnerUserId);
        OwnerUserId = newOwnerUserId;
        MyDayDate = newOwnerMyDayDate;
        SetMyDayDate(previousOwnerUserId, previousOwnerMyDayDate);
    }
}
