using System.Xml.Linq;
using DiskCheckTW.Core.Scheduling;

namespace DiskCheckTW.Tests;

public class ScheduleRulesTests
{
    [Theory]
    [InlineData(1, 12)]
    [InlineData(3, 4)]
    [InlineData(6, 2)]
    public void XmlHasCorrectMonthlyTriggers(int interval, int count)
    {
        var settings = new ScheduleSettings { IntervalMonths = interval, StartAt = new DateTime(2026, 11, 14, 12, 0, 0) };
        var doc = XDocument.Parse(ScheduleRules.TaskXml(settings, "S-1-5-21-123", @"C:\Program Files\DiskCheck TW\DiskCheckTW.App.exe"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var calendars = doc.Descendants(ns + "CalendarTrigger").ToArray();
        Assert.Equal(count, calendars.Length);
        Assert.Equal(ScheduleRules.Months(11, interval).Select(m => System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)).Order(),
            calendars.Select(x => x.Descendants(ns + "Months").Elements().Single().Name.LocalName).Order());
        Assert.Equal("InteractiveToken", doc.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("true", doc.Descendants(ns + "StartWhenAvailable").Single().Value);
        Assert.Equal("PT1H", doc.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.Single(doc.Descendants(ns + "LogonTrigger"));
    }

    [Fact]
    public void MonthEndClampsAndLeapYearWorks()
    {
        var start = new DateTime(2027, 1, 31, 12, 0, 0);
        Assert.Equal(new DateTime(2027, 2, 28, 12, 0, 0), ScheduleRules.Occurrence(start, 1, 1));
        Assert.Equal(new DateTime(2028, 2, 29, 12, 0, 0), ScheduleRules.Occurrence(start, 1, 13));
        var xml = XDocument.Parse(ScheduleRules.TaskXml(new ScheduleSettings { StartAt = start, IntervalMonths = 1 }, "sid", "exe"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Contains(xml.Descendants(ns + "CalendarTrigger"), x =>
            x.Descendants(ns + "February").Any() && x.Descendants(ns + "LastDayOfMonth").Any());
    }

    [Fact]
    public void MissedOnlyAfterOneHourAndSkipAdvancesToFutureCycle()
    {
        var due = new DateTime(2026, 11, 14, 12, 0, 0);
        Assert.False(ScheduleRules.IsMissed(due.AddHours(1), due));
        Assert.True(ScheduleRules.IsMissed(due.AddHours(1).AddTicks(1), due));
        Assert.Equal(new DateTime(2027, 2, 14, 12, 0, 0), ScheduleRules.NextAfter(due, 3, due.AddDays(2)));
    }
}
