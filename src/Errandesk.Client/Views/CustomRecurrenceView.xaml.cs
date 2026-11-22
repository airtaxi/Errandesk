using System.Globalization;
using Errandesk.Client.Helpers;
using Errandesk.Contracts;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Errandesk.Client.Views;

/// <summary>
/// The content of the dialog that sets a custom repeat rule: an interval, a unit and, for weekly rules, the days of the week.
/// </summary>
/// <remarks>The unit items follow the order of <see cref="RecurrenceFrequency"/>, so the selected index is the frequency.</remarks>
public sealed partial class CustomRecurrenceView : UserControl
{
    private const double DayButtonWidth = 40;
    private const double DayButtonHeight = 36;

    private readonly Dictionary<DayOfWeek, ToggleButton> _dayToggleButtons = [];

    public CustomRecurrenceView(TaskRecurrence initialRecurrence)
    {
        InitializeComponent();
        var selectedDays = RecurrenceDisplayHelper.SortDays(initialRecurrence.DaysOfWeek);
        foreach (var dayOfWeek in RecurrenceDisplayHelper.DaysMondayFirst)
        {
            var toggleButton = new ToggleButton { Content = RecurrenceDisplayHelper.GetShortDayName(dayOfWeek), IsChecked = selectedDays.Contains(dayOfWeek), Padding = new Thickness(0), Width = DayButtonWidth, Height = DayButtonHeight };
            AutomationProperties.SetAutomationId(toggleButton, $"RecurrenceDayToggleButton_{dayOfWeek}");
            AutomationProperties.SetName(toggleButton, CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(dayOfWeek));
            _dayToggleButtons[dayOfWeek] = toggleButton;
            RecurrenceDayButtonsPanel.Children.Add(toggleButton);
        }

        RecurrenceIntervalNumberBox.Value = Math.Max(1, initialRecurrence.Interval);
        RecurrenceFrequencyComboBox.SelectedIndex = (int)initialRecurrence.Frequency;
        UpdateDaysVisibility();
    }

    /// <summary>
    /// The rule the controls describe. A weekly rule without chosen days repeats on the weekday of the due date.
    /// </summary>
    public TaskRecurrence SelectedRecurrence
    {
        get
        {
            var frequency = RecurrenceFrequencyComboBox.SelectedIndex is >= 0 and <= (int)RecurrenceFrequency.Yearly ? (RecurrenceFrequency)RecurrenceFrequencyComboBox.SelectedIndex : RecurrenceFrequency.Weekly;
            var interval = double.IsNaN(RecurrenceIntervalNumberBox.Value) ? 1 : (int)Math.Clamp(Math.Round(RecurrenceIntervalNumberBox.Value), 1, 999);
            List<DayOfWeek> days = frequency == RecurrenceFrequency.Weekly ? [.. RecurrenceDisplayHelper.DaysMondayFirst.Where(day => _dayToggleButtons[day].IsChecked == true)] : [];
            return new TaskRecurrence(frequency, interval, days);
        }
    }

    private void OnRecurrenceFrequencyComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDaysVisibility();

    private void UpdateDaysVisibility() => RecurrenceDaysPanel.Visibility = RecurrenceFrequencyComboBox.SelectedIndex == (int)RecurrenceFrequency.Weekly ? Visibility.Visible : Visibility.Collapsed;
}
