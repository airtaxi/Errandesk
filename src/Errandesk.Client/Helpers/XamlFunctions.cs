using System.Globalization;
using Windows.UI.Text;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Functions used by compiled bindings in XAML.
/// </summary>
public static class XamlFunctions
{
    public static Visibility VisibleWhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Strikes through the title of a completed task.
    /// </summary>
    public static TextDecorations GetTitleDecorations(bool isCompleted) => isCompleted ? TextDecorations.Strikethrough : TextDecorations.None;

    /// <summary>
    /// Dims completed items that cannot show a strikethrough, such as editable step titles.
    /// </summary>
    public static double GetCompletedOpacity(bool isCompleted) => isCompleted ? 0.6 : 1;

    public static string GetMyDayButtonText(bool isInMyDay) => LocalizedStrings.Get(isInMyDay ? "RemoveFromMyDayText" : "AddToMyDayText");

    public static string GetImportanceButtonText(bool isImportant) => LocalizedStrings.Get(isImportant ? "RemoveImportanceText" : "MarkImportantText");

    /// <summary>
    /// The text of the reminder button: "Remind me", or the reminder time when one is set.
    /// </summary>
    public static string GetReminderButtonText(string reminderText) => string.IsNullOrEmpty(reminderText) ? LocalizedStrings.Get("RemindMeText") : string.Format(CultureInfo.CurrentCulture, LocalizedStrings.Get("RemindAtFormat"), reminderText);

    /// <summary>
    /// The text of the repeat button: "Repeat", or the repeat rule when one is set.
    /// </summary>
    public static string GetRecurrenceButtonText(string recurrenceText) => string.IsNullOrEmpty(recurrenceText) ? LocalizedStrings.Get("RepeatText") : recurrenceText;

    public static string GetCompletedSectionGlyph(bool isExpanded) => isExpanded ? "\uE70D" : "\uE76C";

    public static string GetPinButtonText(bool isPinned) => LocalizedStrings.Get(isPinned ? "UnpinNoteText" : "PinNoteText");

    public static string GetPinButtonGlyph(bool isPinned) => isPinned ? "\uE77A" : "\uE718";
}
