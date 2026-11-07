using System.Xml.Linq;

namespace DiskCheckTW.Core.Scheduling;

public sealed class ScheduleSettings
{
    public bool Enabled { get; set; }
    public int IntervalMonths { get; set; } = 3;
    public DateTime StartAt { get; set; }
    public bool FullScan { get; set; }
    public DateTime NextAt { get; set; }
    public DateTime? LastAt { get; set; }
    public string? LastResult { get; set; }
}

public static class ScheduleRules
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static IReadOnlyList<int> Months(int startMonth, int intervalMonths)
    {
        if (startMonth is < 1 or > 12 || intervalMonths is not (1 or 3 or 6))
            throw new ArgumentOutOfRangeException(nameof(intervalMonths));
        return Enumerable.Range(0, 12 / intervalMonths)
            .Select(i => (startMonth - 1 + i * intervalMonths) % 12 + 1).Order().ToArray();
    }

    public static DateTime Occurrence(DateTime start, int intervalMonths, int cycle)
    {
        if (intervalMonths is not (1 or 3 or 6) || cycle < 0) throw new ArgumentOutOfRangeException(nameof(intervalMonths));
        var month = new DateTime(start.Year, start.Month, 1, start.Hour, start.Minute, 0).AddMonths(checked(cycle * intervalMonths));
        return month.AddDays(Math.Min(start.Day, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
    }

    public static DateTime NextAfter(DateTime start, int intervalMonths, DateTime after)
    {
        for (var cycle = 0; cycle < 10000; cycle++)
        {
            var due = Occurrence(start, intervalMonths, cycle);
            if (due > after) return due;
        }
        throw new ArgumentOutOfRangeException(nameof(after));
    }

    public static bool IsMissed(DateTime now, DateTime due) => now > due.AddHours(1);

    public static string TaskXml(ScheduleSettings settings, string userSid, string exePath)
    {
        if (settings.IntervalMonths is not (1 or 3 or 6) || settings.StartAt == default || string.IsNullOrWhiteSpace(userSid) || string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Schedule settings are incomplete.");
        var triggers = Months(settings.StartAt.Month, settings.IntervalMonths).Select(month =>
        {
            var day = settings.StartAt.Day;
            var lastDay = day > DateTime.DaysInMonth(2024, month);
            var days = lastDay ? new XElement(Ns + "LastDayOfMonth") : new XElement(Ns + "Day", day);
            return new XElement(Ns + "CalendarTrigger",
                new XElement(Ns + "StartBoundary", settings.StartAt.ToString("yyyy-MM-ddTHH:mm:ss")),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "ScheduleByMonth",
                    new XElement(Ns + "DaysOfMonth", days),
                    new XElement(Ns + "Months", new XElement(Ns + System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)))));
        }).ToList();
        // 登入觸發讓「稍後」在下次登入仍有機會提示；程式會用 NextAt 擋掉未到期的啟動。
        triggers.Add(new XElement(Ns + "LogonTrigger", new XElement(Ns + "Enabled", "true"), new XElement(Ns + "UserId", userSid)));
        var document = new XDocument(new XDeclaration("1.0", "utf-16", null),
            new XElement(Ns + "Task", new XAttribute("version", "1.4"),
                new XElement(Ns + "Triggers", triggers),
                new XElement(Ns + "Principals", new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userSid), new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "true"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "StartWhenAvailable", "true"),
                    new XElement(Ns + "ExecutionTimeLimit", settings.FullScan ? "PT12H" : "PT1H")),
                new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec", new XElement(Ns + "Command", exePath), new XElement(Ns + "Arguments", "--scheduled")))));
        return document.Declaration + Environment.NewLine + document;
    }
}
