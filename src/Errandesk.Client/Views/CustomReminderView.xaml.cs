using Errandesk.Client.Helpers;

namespace Errandesk.Client.Views;

/// <summary>
/// The content of the dialog that sets a reminder on a chosen date and time.
/// </summary>
public sealed partial class CustomReminderView : UserControl
{
    public CustomReminderView(DateTimeOffset initialReminder)
    {
        InitializeComponent();
        var localReminder = initialReminder.ToLocalTime();
        ReminderDatePicker.Date = new DateTimeOffset(localReminder.Date, localReminder.Offset);
        ReminderTimePicker.Time = localReminder.TimeOfDay;
    }

    /// <summary>
    /// The chosen point in time, or <see langword="null"/> when no date is chosen.
    /// </summary>
    public DateTimeOffset? SelectedReminder => DateDisplayHelper.ToDateOnly(ReminderDatePicker.Date) is { } date ? ReminderTimeHelper.Combine(date, ReminderTimePicker.Time) : null;
}
