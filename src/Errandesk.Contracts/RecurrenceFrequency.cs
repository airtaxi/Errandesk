using System.Text.Json.Serialization;

namespace Errandesk.Contracts;

/// <summary>
/// How often a repeating task comes back.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecurrenceFrequency>))]
public enum RecurrenceFrequency
{
    [JsonStringEnumMemberName("daily")]
    Daily,

    /// <summary>Every week on the chosen days; Monday to Friday is the "weekdays" repeat.</summary>
    [JsonStringEnumMemberName("weekly")]
    Weekly,

    /// <summary>Every month on the day of the month of the due date, or the month's last day when it is shorter.</summary>
    [JsonStringEnumMemberName("monthly")]
    Monthly,

    /// <summary>Every year on the date of the due date; February 29 falls on February 28 in common years.</summary>
    [JsonStringEnumMemberName("yearly")]
    Yearly,
}
