using System.Globalization;
using Errandesk.Contracts;
using Errandesk.Server.Data;
using Errandesk.Server.Services;

namespace Errandesk.Server.Tests.Services;

public sealed class RecurrenceCalculatorTests
{
    [Theory]
    [InlineData("2031-03-10", 1, "2031-03-11")]
    [InlineData("2031-03-10", 3, "2031-03-13")]
    [InlineData("2031-12-31", 1, "2032-01-01")]
    [InlineData("2032-02-28", 1, "2032-02-29")]
    public void Daily_AddsTheIntervalInDays(string dueDate, int interval, string expectedDate) => AssertNext(dueDate, new TaskRecurrenceDocument { Frequency = RecurrenceFrequency.Daily, Interval = interval }, expectedDate);

    [Theory]
    [InlineData("2031-03-10", "2031-03-11")]
    [InlineData("2031-03-13", "2031-03-14")]
    [InlineData("2031-03-14", "2031-03-17")]
    [InlineData("2031-03-15", "2031-03-17")]
    [InlineData("2031-03-16", "2031-03-17")]
    public void Weekdays_MovesToTheNextDayFromMondayToFriday(string dueDate, string expectedDate) => AssertNext(dueDate, new TaskRecurrenceDocument { Frequency = RecurrenceFrequency.Weekly, DaysOfWeek = [.. TaskRecurrenceRules.Weekdays] }, expectedDate);

    [Theory]
    [InlineData("2031-03-10", 1, "2031-03-13")]
    [InlineData("2031-03-13", 1, "2031-03-17")]
    [InlineData("2031-03-10", 2, "2031-03-13")]
    [InlineData("2031-03-13", 2, "2031-03-24")]
    [InlineData("2031-03-15", 2, "2031-03-24")]
    public void WeeklyWithDays_UsesTheNextDayOfTheSameWeekOrTheFirstDayOfTheWeekTheIntervalLater(string dueDate, int interval, string expectedDate) => AssertNext(dueDate, new TaskRecurrenceDocument { Frequency = RecurrenceFrequency.Weekly, Interval = interval, DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Thursday] }, expectedDate);

    [Fact]
    public void WeeklyWithDays_TreatsSundayAsTheLastDayOfTheWeek() => AssertNext("2031-03-10", new TaskRecurrenceDocument { Frequency = RecurrenceFrequency.Weekly, DaysOfWeek = [DayOfWeek.Sunday, DayOfWeek.Monday] }, "2031-03-16");

    [Theory]
    [InlineData("2031-03-10", 1, "2031-03-17")]
    [InlineData("2031-03-10", 2, "2031-03-24")]
    [InlineData("2031-12-29", 1, "2032-01-05")]
    public void WeeklyWithoutDays_RepeatsOnTheWeekdayOfTheDueDate(string dueDate, int interval, string expectedDate) => AssertNext(dueDate, new TaskRecurrenceDocument { Frequency = RecurrenceFrequency.Weekly, Interval = interval }, expectedDate);

    [Theory]
    [InlineData("2031-01-15", 15, 1, "2031-02-15")]
    [InlineData("2031-01-31", 31, 1, "2031-02-28")]
    [InlineData("2031-02-28", 31, 1, "2031-03-31")]
    [InlineData("2031-03-31", 31, 1, "2031-04-30")]
    [InlineData("2031-04-30", 31, 1, "2031-05-31")]
    [InlineData("2032-01-31", 31, 1, "2032-02-29")]
    [InlineData("2031-11-30", 30, 3, "2032-02-29")]
    [InlineData("2031-12-31", 31, 1, "2032-01-31")]
    [InlineData("2031-01-31", 0, 1, "2031-02-28")]
    public void Monthly_ReturnsToTheAnchorDayAndUsesTheLastDayOfShorterMonths(string dueDate, int anchorDayOfMonth, int interval, string expectedDate) => AssertNext(dueDate, new TaskRecurrenceDocument { Frequency = RecurrenceFrequency.Monthly, Interval = interval, AnchorDayOfMonth = anchorDayOfMonth }, expectedDate);

    [Theory]
    [InlineData("2031-07-04", 4, 1, "2032-07-04")]
    [InlineData("2028-02-29", 29, 1, "2029-02-28")]
    [InlineData("2029-02-28", 29, 1, "2030-02-28")]
    [InlineData("2031-02-28", 29, 1, "2032-02-29")]
    [InlineData("2028-02-29", 29, 4, "2032-02-29")]
    [InlineData("2029-02-28", 28, 3, "2032-02-28")]
    public void Yearly_KeepsTheDateAndUsesFebruary28InCommonYears(string dueDate, int anchorDayOfMonth, int interval, string expectedDate) => AssertNext(dueDate, new TaskRecurrenceDocument { Frequency = RecurrenceFrequency.Yearly, Interval = interval, AnchorDayOfMonth = anchorDayOfMonth }, expectedDate);

    [Fact]
    public void Monthly_FollowsTheAnchorAcrossSeveralOccurrences()
    {
        var recurrence = TaskRecurrenceRules.ToDocument(TaskRecurrenceRules.Normalize(new TaskRecurrence(RecurrenceFrequency.Monthly)), new DateOnly(2031, 1, 31));
        var dueDate = new DateOnly(2031, 1, 31);
        List<DateOnly> dueDates = [];
        for (var occurrence = 0; occurrence < 4; occurrence++)
        {
            dueDate = RecurrenceCalculator.GetNextDueDate(dueDate, recurrence);
            dueDates.Add(dueDate);
        }

        Assert.Equal([new DateOnly(2031, 2, 28), new DateOnly(2031, 3, 31), new DateOnly(2031, 4, 30), new DateOnly(2031, 5, 31)], dueDates);
    }

    [Theory]
    [InlineData("2031-03-10", RecurrenceFrequency.Daily, "", "2031-03-10")]
    [InlineData("2031-03-16", RecurrenceFrequency.Monthly, "", "2031-03-16")]
    [InlineData("2031-03-16", RecurrenceFrequency.Weekly, "", "2031-03-16")]
    [InlineData("2031-03-10", RecurrenceFrequency.Weekly, "Monday,Thursday", "2031-03-10")]
    [InlineData("2031-03-11", RecurrenceFrequency.Weekly, "Monday,Thursday", "2031-03-13")]
    [InlineData("2031-03-15", RecurrenceFrequency.Weekly, "Monday,Tuesday,Wednesday,Thursday,Friday", "2031-03-17")]
    public void GetFirstDueDate_IsTodayOrTheFirstChosenWeekdayFromToday(string today, RecurrenceFrequency frequency, string days, string expectedDate)
    {
        IReadOnlyList<DayOfWeek> daysOfWeek = [.. days.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<DayOfWeek>)];

        var firstDueDate = RecurrenceCalculator.GetFirstDueDate(DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture), new TaskRecurrence(frequency, 1, daysOfWeek));

        Assert.Equal(DateOnly.ParseExact(expectedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), firstDueDate);
    }

    [Fact]
    public void Normalize_OrdersDaysFromMondayAndRejectsInvalidRules()
    {
        var normalized = TaskRecurrenceRules.Normalize(new TaskRecurrence(RecurrenceFrequency.Weekly, 2, [DayOfWeek.Sunday, DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Sunday]));

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Sunday], normalized.DaysOfWeek);
        Assert.Throws<RequestValidationException>(() => TaskRecurrenceRules.Normalize(new TaskRecurrence(RecurrenceFrequency.Daily, 0)));
        Assert.Throws<RequestValidationException>(() => TaskRecurrenceRules.Normalize(new TaskRecurrence(RecurrenceFrequency.Daily, TaskRecurrenceRules.MaximumInterval + 1)));
        Assert.Throws<RequestValidationException>(() => TaskRecurrenceRules.Normalize(new TaskRecurrence(RecurrenceFrequency.Monthly, 1, [DayOfWeek.Monday])));
        Assert.Throws<RequestValidationException>(() => TaskRecurrenceRules.Normalize(new TaskRecurrence((RecurrenceFrequency)42)));
        Assert.Throws<RequestValidationException>(() => TaskRecurrenceRules.Normalize(new TaskRecurrence(RecurrenceFrequency.Weekly, 1, [(DayOfWeek)9])));
    }

    private static void AssertNext(string dueDate, TaskRecurrenceDocument recurrence, string expectedDate) => Assert.Equal(DateOnly.ParseExact(expectedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), RecurrenceCalculator.GetNextDueDate(DateOnly.ParseExact(dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), recurrence));
}
