using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DiskCheckTW.Core.Analysis;
using DiskCheckTW.Core.Models;
using DiskCheckTW.Core.Reporting;
using Xunit;

namespace DiskCheckTW.Tests;

/// <summary>英文介面：文字、報告語言與規則一致性。每個測試自行切換文化並還原。</summary>
public class EnglishTests
{
    private static readonly Regex Cjk = new(@"[㐀-鿿豈-﫿　-〿＀-￯]");

    private static T InCulture<T>(string name, Func<T> body)
    {
        var old = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        try { return body(); } finally { CultureInfo.CurrentUICulture = old; }
    }

    private static SmartAttribute A(byte id, byte cur, ulong raw, byte? th = 10) =>
        new() { Id = id, Name = "", Current = cur, Worst = cur, Threshold = th, Raw = raw };

    private static PhysicalDiskInfo Hdd(params SmartAttribute[] attrs) => new()
    {
        Index = 1, Model = "WDC WD10EZEX", Serial = "WD-ABC12345", SizeBytes = 1_000_204_886_016,
        Kind = DriveKind.Hdd, BusType = "SATA", DriveLetters = new[] { "D:" },
        AtaSmart = new AtaSmartData { Attributes = attrs, SmartReturnStatusFailing = false },
    };

    private static PhysicalDiskInfo Healthy() => Hdd(A(0x05, 100, 0), A(0x09, 99, 1200), A(0x0C, 99, 300), A(0xC2, 40, 0x28));
    private static PhysicalDiskInfo Failing() => Hdd(A(0x05, 5, 900), A(0x09, 99, 1200), A(0xC5, 100, 12));

    [Fact]
    public void English_status_texts()
    {
        Assert.Equal("Good", InCulture("en-US", () => HealthAnalyzer.Analyze(Healthy()).StatusText));
        Assert.Equal("Danger", InCulture("en-US", () => HealthAnalyzer.Analyze(Failing()).StatusText));
    }

    [Fact]
    public void Non_chinese_cultures_fall_back_to_english()
    {
        Assert.Equal("Good", InCulture("ja-JP", () => HealthAnalyzer.Analyze(Healthy()).StatusText));
        Assert.Equal("正常", InCulture("zh-HK", () => HealthAnalyzer.Analyze(Healthy()).StatusText));
    }

    [Fact]
    public void English_findings_and_plan_contain_no_chinese()
    {
        var (diag, plan) = InCulture("en-US", () =>
        {
            var d = HealthAnalyzer.Analyze(Failing());
            return (d, ActionAdvisor.Build(Failing(), d));
        });
        Assert.NotEmpty(diag.Findings);
        foreach (var f in diag.Findings)
            foreach (var text in new[] { f.Title, f.Detail, f.Impact, f.Advice })
                Assert.False(Cjk.IsMatch(text), text);
        Assert.Equal(ActionUrgency.Immediate, plan.Urgency);
        Assert.Equal("Act now", plan.UrgencyText);
        foreach (var text in plan.Steps.SelectMany(s => new[] { s.Title, s.Detail }).Append(plan.Headline))
            Assert.False(Cjk.IsMatch(text), text);
    }

    [Fact]
    public void Advisor_decisions_do_not_depend_on_language()
    {
        foreach (var disk in new[] { Healthy(), Failing(), Hdd(A(0x05, 100, 0), A(0xC7, 100, 3)) })
        {
            var zh = InCulture("zh-TW", () => ActionAdvisor.Build(disk, HealthAnalyzer.Analyze(disk)));
            var en = InCulture("en-US", () => ActionAdvisor.Build(disk, HealthAnalyzer.Analyze(disk)));
            Assert.Equal(zh.Urgency, en.Urgency);
            Assert.Equal(zh.Steps.Count, en.Steps.Count);
        }
    }

    [Fact]
    public void English_report_is_english()
    {
        var html = InCulture("en-US", () =>
        {
            var d = Failing(); var diag = HealthAnalyzer.Analyze(d);
            return ReportGenerator.GenerateHtml(d, diag, null,
                new ReportContext { GeneratedAt = new DateTime(2026, 11, 7, 12, 0, 0), OsDescription = "Windows 10", AppVersion = "0.1.3" });
        });
        Assert.Contains("lang=\"en\"", html);
        var text = Regex.Replace(Regex.Replace(html, "<style.*?</style>", "", RegexOptions.Singleline), "<[^>]+>", " ");
        Assert.False(Cjk.IsMatch(text), Cjk.Match(text).Value);
    }

    [Fact]
    public void Smart_names_are_english_only_in_english()
    {
        var en = InCulture("en-US", () => SmartProfiles.NameOf("Smart", 0x05));
        Assert.DoesNotContain(" / ", en);
        Assert.False(Cjk.IsMatch(en));
        Assert.Contains(" / ", InCulture("zh-TW", () => SmartProfiles.NameOf("Smart", 0x05)));
    }

    [Theory]
    [InlineData("src/DiskCheckTW.Core")]
    [InlineData("src/DiskCheckTW.App")]
    public void Resource_keys_match_between_languages(string project)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "DiskCheckTW.slnx"))) dir = Path.GetDirectoryName(dir)!;
        static HashSet<string> Keys(string path) =>
            XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
        var en = Keys(Path.Combine(dir, project, "Strings.resx"));
        var zh = Keys(Path.Combine(dir, project, "Strings.zh-TW.resx"));
        Assert.True(en.SetEquals(zh), string.Join(",", en.Except(zh).Concat(zh.Except(en))));
        Assert.True(en.Count > 100);
    }
}
