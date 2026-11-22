using Errandesk.Contracts;
using Errandesk.Server.Import;

namespace Errandesk.Server.Tests.Import;

public sealed class MicrosoftTodoTaskMapperTests
{
    [Theory]
    [InlineData("2031-03-20T00:00:00.0000000", "UTC", "America/Los_Angeles", "2031-03-20")]
    [InlineData("2031-03-20T00:00:00.0000000", "UTC", "Asia/Seoul", "2031-03-20")]
    [InlineData("2031-03-20T04:00:00.0000000", "UTC", "America/New_York", "2031-03-20")]
    [InlineData("2031-03-19T15:00:00.0000000", "UTC", "Asia/Seoul", "2031-03-20")]
    [InlineData("2031-03-20T00:00:00.0000000", "Pacific Standard Time", "Asia/Seoul", "2031-03-20")]
    public void MapDueDate_TakesMidnightAsTheDateAndConvertsOtherTimesToTheUsersTimeZone(string dateTime, string timeZone, string userTimeZone, string expectedDate) => Assert.Equal(DateOnly.Parse(expectedDate), MicrosoftTodoTaskMapper.MapDueDate(new GraphDateTimeTimeZone(dateTime, timeZone), userTimeZone));

    [Fact]
    public void MapDueDate_WithoutAValue_IsNull()
    {
        Assert.Null(MicrosoftTodoTaskMapper.MapDueDate(null, "UTC"));
        Assert.Null(MicrosoftTodoTaskMapper.MapDueDate(new GraphDateTimeTimeZone("not a date", "UTC"), "UTC"));
    }

    [Theory]
    [InlineData("2031-07-01T09:00:00.0000000", "Pacific Standard Time", "2031-07-01T16:00:00Z")]
    [InlineData("2031-07-01T09:00:00.0000000", "Asia/Seoul", "2031-07-01T00:00:00Z")]
    [InlineData("2031-07-01T09:00:00.0000000", "Unknown/Zone", "2031-07-01T09:00:00Z")]
    [InlineData("2031-07-01T09:00:00Z", "Asia/Seoul", "2031-07-01T09:00:00Z")]
    public void ToUtc_ConvertsWindowsAndIanaTimeZonesAndTreatsUnknownZonesAsUtc(string dateTime, string timeZone, string expectedUtc) => Assert.Equal(DateTime.Parse(expectedUtc).ToUniversalTime(), MicrosoftTodoTaskMapper.ToUtc(new GraphDateTimeTimeZone(dateTime, timeZone)));

    [Fact]
    public void MapRecurrence_MapsDailyWeeklyMonthlyAndYearlyAndLeavesOutRelativePatterns()
    {
        var daily = MicrosoftTodoTaskMapper.MapRecurrence(new GraphRecurrencePattern("daily", 2, null));
        Assert.Equal((RecurrenceFrequency.Daily, 2), (daily?.Frequency, daily?.Interval));
        Assert.Empty(daily?.DaysOfWeek ?? []);

        var weekly = MicrosoftTodoTaskMapper.MapRecurrence(new GraphRecurrencePattern("weekly", 1, ["friday", "Monday", "monday", "someday"]));
        Assert.Equal(RecurrenceFrequency.Weekly, weekly?.Frequency);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], weekly?.DaysOfWeek ?? []);

        Assert.Equal(RecurrenceFrequency.Monthly, MicrosoftTodoTaskMapper.MapRecurrence(new GraphRecurrencePattern("absoluteMonthly", 3, null))?.Frequency);
        Assert.Equal(RecurrenceFrequency.Yearly, MicrosoftTodoTaskMapper.MapRecurrence(new GraphRecurrencePattern("absoluteYearly", 1, null))?.Frequency);
        Assert.Equal(1, MicrosoftTodoTaskMapper.MapRecurrence(new GraphRecurrencePattern("daily", 0, null))?.Interval);
        Assert.Null(MicrosoftTodoTaskMapper.MapRecurrence(new GraphRecurrencePattern("relativeMonthly", 1, ["monday"])));
        Assert.Null(MicrosoftTodoTaskMapper.MapRecurrence(new GraphRecurrencePattern("relativeYearly", 1, ["monday"])));
        Assert.Null(MicrosoftTodoTaskMapper.MapRecurrence(null));
    }

    [Fact]
    public void MapBody_StripsHtmlAndNormalizesLineBreaks()
    {
        Assert.Equal("Shopping\nMilk & eggs\n\nBread", MicrosoftTodoTaskMapper.MapBody(new GraphItemBody("<div>Shopping</div><ul><li>Milk &amp; eggs</li></ul><p></p><p>Bread&nbsp;</p>", "html")));
        Assert.Equal("First\nSecond", MicrosoftTodoTaskMapper.MapBody(new GraphItemBody("  First\r\nSecond  ", "text")));
        Assert.Equal("", MicrosoftTodoTaskMapper.MapBody(null));
    }

    [Fact]
    public void MapListName_TrimsAndFallsBackForBlankNames()
    {
        Assert.Equal("Groceries", MicrosoftTodoTaskMapper.MapListName("  Groceries "));
        Assert.Equal(MicrosoftTodoTaskMapper.UntitledListName, MicrosoftTodoTaskMapper.MapListName("   "));
        Assert.Equal(256, MicrosoftTodoTaskMapper.MapListName(new string('a', 300)).Length);
    }
}
