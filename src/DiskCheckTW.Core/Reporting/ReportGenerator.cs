using DiskCheckTW.Core;
namespace DiskCheckTW.Core.Reporting;

using System.Globalization;
using System.Net;
using System.Text;
using DiskCheckTW.Core.Analysis;
using DiskCheckTW.Core.Models;

/// <summary>報告頁首資訊：產生時間、作業系統、程式版本、序號是否遮罩。</summary>
public sealed class ReportContext
{
    public DateTime GeneratedAt { get; init; }
    public string OsDescription { get; init; } = "";
    public string AppVersion { get; init; } = "";
    public bool MaskSerial { get; init; } = true;
}

/// <summary>把診斷與掃描結果組成單一、離線、可列印的 HTML 字串（不寫檔、不連網）。</summary>
public static class ReportGenerator
{
    private static string Unknown => S.T("R001");
    private const double BytesPerGb = 1_000_000_000d;
    private const double BytesPerDataUnit = 512_000d;
    private const int MaxErrorOffsets = 50;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string GenerateHtml(PhysicalDiskInfo disk, Diagnosis diagnosis, ScanResult? scan, ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(diagnosis);
        ArgumentNullException.ThrowIfNull(context);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n")
          .Append($"<html lang=\"{(S.Language.Name == "zh-TW" ? "zh-Hant" : "en")}\">\n<head>\n")
          .Append("""<meta charset="utf-8">""").Append("\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append(S.T("R002"))
          .Append("<style>\n").Append(Styles).Append("</style>\n</head>\n<body>\n");

        AppendHeader(sb, context);
        AppendDisk(sb, disk, context);
        AppendHealth(sb, diagnosis, disk);
        AppendActions(sb, ActionAdvisor.Build(disk, diagnosis, scan), context.GeneratedAt);
        AppendFindings(sb, diagnosis);
        AppendSmart(sb, disk);
        AppendScan(sb, scan);

        sb.Append(S.T("R003"))
          .Append(S.T("R004"))
          .Append(OfficialLinks.OfficialSite).Append("\">keelungit.com/diskcheck-tw/</a></p>\n")
          .Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>序號遮罩：保留前 4 個字元，其餘換成 *。</summary>
    public static string MaskSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return Unknown;
        if (serial.Length <= 4)
            return new string('*', serial.Length);
        return serial[..4] + new string('*', serial.Length - 4);
    }

    private static void AppendHeader(StringBuilder sb, ReportContext context)
    {
        sb.Append(S.T("R005"));
        Meta(sb, S.T("R006"), Text(context.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv)));
        Meta(sb, S.T("R007"), Text(context.OsDescription));
        Meta(sb, S.T("R008"), Text(context.AppVersion));
    }

    private static void AppendDisk(StringBuilder sb, PhysicalDiskInfo disk, ReportContext context)
    {
        sb.Append(S.T("R009"));
        Row(sb, S.T("R010"), Text(disk.Model));
        Row(sb, S.T("R011"), Text(disk.Vendor));
        Row(sb, S.T("R012"), $"{(disk.SizeBytes / BytesPerGb).ToString("0.0", Inv)} GB" + S.Paren($"{disk.SizeBytes.ToString("N0", Inv)} bytes"));
        Row(sb, S.T("R013"), KindLabel(disk.Kind));
        Row(sb, S.T("R014"), disk.BusType == "Unknown" ? S.T("R075") : Text(disk.BusType));
        Row(sb, S.T("R015"), context.MaskSerial ? Text(MaskSerial(disk.Serial)) : Text(disk.Serial));
        Row(sb, S.T("R016"), Text(disk.Firmware));
        Row(sb, S.T("R017"), disk.DriveLetters.Count == 0 ? S.T("R018") : Text(string.Join(S.ListSep, disk.DriveLetters)));
        Row(sb, S.T("R019"), disk.IsSystemDisk ? S.T("R020") : S.T("R021"));
        sb.Append("</table>\n");
    }

    private static void AppendHealth(StringBuilder sb, Diagnosis diagnosis, PhysicalDiskInfo disk)
    {
        sb.Append(S.T("R022"));
        sb.Append("<p class=\"status ").Append(StatusClass(diagnosis.Status)).Append("\">")
          .Append(Text(diagnosis.StatusText)).Append("</p>\n");
        sb.Append("<p>").Append(Text(diagnosis.Summary)).Append("</p>\n");
        sb.Append("<table>\n");
        Row(sb, S.T("R023"), diagnosis.TemperatureCelsius is int c ? $"{c} °C" : Unknown);
        Row(sb, S.T("R024"), diagnosis.PowerOnHours is ulong h ? S.F("R025", h.ToString(Inv)) : Unknown);
        Row(sb, S.T("R026"), diagnosis.PowerCycles is ulong p ? p.ToString(Inv) : Unknown);
        Row(sb, S.T("R027"), diagnosis.LifeRemainingPercent is int life ? $"{life} %" : Unknown);
        sb.Append("</table>\n");
        if (!string.IsNullOrWhiteSpace(disk.HealthUnavailableReason))
            sb.Append(S.T("R028")).Append(Text(disk.HealthUnavailableReason)).Append("</p>\n");
    }

    private static void AppendFindings(StringBuilder sb, Diagnosis diagnosis)
    {
        sb.Append(S.T("R029"));
        if (diagnosis.Findings.Count == 0)
        {
            sb.Append(S.T("R030"));
            return;
        }
        foreach (var f in diagnosis.Findings)
        {
            sb.Append("<div class=\"finding ").Append(SeverityClass(f.Severity)).Append("\">\n")
              .Append("<p class=\"finding-title\">").Append(Text(f.Title)).Append("</p>\n")
              .Append(S.T("R031")).Append(Text(f.Detail)).Append("</p>\n")
              .Append(S.T("R032")).Append(Text(f.Impact)).Append("</p>\n")
              .Append(S.T("R033")).Append(Text(f.Advice)).Append("</p>\n")
              .Append("</div>\n");
        }
    }

    private static void AppendActions(StringBuilder sb, ActionPlan plan, DateTime generatedAt)
    {
        var urgencyClass = plan.Urgency switch
        {
            ActionUrgency.Immediate => "action-immediate",
            ActionUrgency.Soon => "action-soon",
            ActionUrgency.Routine => "action-routine",
            _ => "action-checksetup"
        };
        sb.Append("<section class=\"action ").Append(urgencyClass).Append("\">\n")
          .Append(S.T("R034"))
          .Append("<p class=\"action-urgency\">").Append(WebUtility.HtmlEncode(plan.UrgencyText)).Append("</p>\n")
          .Append("<p class=\"action-headline\">").Append(WebUtility.HtmlEncode(plan.Headline)).Append("</p>\n<ol>\n");
        foreach (var step in plan.Steps)
            sb.Append("<li><strong>").Append(WebUtility.HtmlEncode(step.Title)).Append("</strong><br>")
              .Append(WebUtility.HtmlEncode(step.Detail)).Append("</li>\n");
        sb.Append("</ol>\n");
        if (plan.Urgency == ActionUrgency.Routine)
            sb.Append(S.T("R035"))
              .Append(generatedAt.AddMonths(ActionRules.FullCheckIntervalMonths).ToString("yyyy-MM-dd", Inv))
              .Append("</p>\n");
        sb.Append("</section>\n");
    }

    private static void AppendSmart(StringBuilder sb, PhysicalDiskInfo disk)
    {
        sb.Append(S.T("R036"));
        if (disk.AtaSmart is null && disk.NvmeHealth is null)
        {
            sb.Append(S.T("R037"));
            return;
        }
        if (disk.AtaSmart is { } ata)
        {
            sb.Append("<h3>ATA SMART</h3>\n<table>\n")
              .Append(S.T("R038"));
            foreach (var a in ata.Attributes)
            {
                sb.Append("<tr>")
                  .Append("<td>").Append(a.Id.ToString("X2", Inv)).Append("</td>")
                  .Append("<td>").Append(Text(a.Name)).Append("</td>")
                  .Append("<td>").Append(a.Current.ToString(Inv)).Append("</td>")
                  .Append("<td>").Append(a.Worst.ToString(Inv)).Append("</td>")
                  .Append("<td>").Append(a.Threshold is byte t ? t.ToString(Inv) : "—").Append("</td>")
                  .Append("<td>").Append(a.Raw.ToString(Inv)).Append("</td>")
                  .Append("</tr>\n");
            }
            sb.Append("</table>\n");
        }
        if (disk.NvmeHealth is { } nvme)
        {
            sb.Append(S.T("R039"));
            Row(sb, S.T("R040"), $"0x{nvme.CriticalWarning:X2}");
            Row(sb, S.T("R041"), nvme.TemperatureCelsius is int c ? $"{c} °C" : Unknown);
            Row(sb, S.T("R042"), $"{nvme.AvailableSpare} %");
            Row(sb, S.T("R043"), $"{nvme.AvailableSpareThreshold} %");
            Row(sb, S.T("R044"), $"{nvme.PercentageUsed} %");
            Row(sb, S.T("R045"), nvme.MediaErrors.ToString(Inv));
            Row(sb, S.T("R046"), nvme.ErrorLogEntries.ToString(Inv));
            Row(sb, S.T("R047"), S.F("R048", nvme.PowerOnHours.ToString(Inv)));
            Row(sb, S.T("R049"), nvme.PowerCycles.ToString(Inv));
            Row(sb, S.T("R050"), nvme.UnsafeShutdowns.ToString(Inv));
            Row(sb, S.T("R051"), DataUnitLabel(nvme.DataUnitsRead));
            Row(sb, S.T("R052"), DataUnitLabel(nvme.DataUnitsWritten));
            sb.Append("</table>\n");
        }
    }

    private static void AppendScan(StringBuilder sb, ScanResult? scan)
    {
        sb.Append(S.T("R053"));
        if (scan is null)
        {
            sb.Append(S.T("R054"));
            return;
        }
        sb.Append("<table>\n");
        Row(sb, S.T("R055"), scan.Mode == ScanMode.Full ? S.T("R056") : S.T("R057"));
        Row(sb, S.T("R058"), Text(scan.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv)));
        Row(sb, S.T("R059"), ElapsedLabel(scan.Elapsed));
        Row(sb, S.T("R060"), $"{(scan.BytesScanned / BytesPerGb).ToString("0.0", Inv)} GB");
        Row(sb, S.T("R061"), scan.BlocksTotal.ToString(Inv));
        Row(sb, S.T("R062"), scan.OkBlocks.ToString(Inv));
        Row(sb, S.T("R063"), scan.SlowBlocks.ToString(Inv));
        Row(sb, S.T("R064"), scan.ErrorBlocks.ToString(Inv));
        Row(sb, S.T("R065"), $"{scan.AvgLatencyMs.ToString("0.0", Inv)} ms");
        Row(sb, S.T("R066"), $"{scan.MaxLatencyMs.ToString("0.0", Inv)} ms");
        Row(sb, S.T("R067"), scan.Cancelled ? S.T("R068") : S.T("R069"));
        Row(sb, S.T("R070"), scan.Cancelled && scan.AbortReason is null ? S.T("R071") : Text(scan.AbortReason));
        sb.Append("</table>\n");
        if (scan.ErrorOffsets.Count > 0)
        {
            var offsets = scan.ErrorOffsets.Take(MaxErrorOffsets);
            sb.Append(S.T("R072")).Append(MaxErrorOffsets.ToString(Inv)).Append(S.T("R073"))
              .Append(Text(string.Join(S.ListSep, offsets))).Append("</p>\n");
        }
    }

    // 表格列：呼叫端已把它包在 <table> 內
    private static void Row(StringBuilder sb, string label, string value) =>
        sb.Append("<tr><th>").Append(label).Append("</th><td>").Append(value).Append("</td></tr>\n");

    // 頁首的純文字列（不在 table 內）
    private static void Meta(StringBuilder sb, string label, string value) =>
        sb.Append("<p class=\"meta\">").Append(label).Append(S.Colon).Append(value).Append("</p>\n");

    private static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Unknown : WebUtility.HtmlEncode(value);

    private static string DataUnitLabel(ulong units) =>
        $"{(units * BytesPerDataUnit / BytesPerGb).ToString("0.0", Inv)} GB";

    private static string ElapsedLabel(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private static string KindLabel(DriveKind kind) => kind switch
    {
        DriveKind.Hdd => S.T("R074"),
        DriveKind.SataSsd => "SATA SSD",
        DriveKind.NvmeSsd => "NVMe SSD",
        _ => S.T("R075"),
    };

    private static string StatusClass(HealthStatus status) => status switch
    {
        HealthStatus.Normal => "status-normal",
        HealthStatus.Caution => "status-caution",
        HealthStatus.Danger => "status-danger",
        _ => "status-unknown",
    };

    private static string SeverityClass(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Danger => "finding-danger",
        FindingSeverity.Caution => "finding-caution",
        _ => "finding-info",
    };

    private const string Styles = """
        body{font-family:'Segoe UI','Microsoft JhengHei',sans-serif;margin:24px;background:#fff;color:#1f2328;font-size:14px;line-height:1.6}
        h1{font-size:24px;margin:0 0 12px}
        h2{font-size:18px;margin:22px 0 6px;border-bottom:2px solid #d0d7de;padding-bottom:4px}
        h3{font-size:15px;margin:14px 0 4px}
        p{margin:4px 0}
        .meta{color:#57606a}
        table{border-collapse:collapse;width:100%;margin:6px 0}
        th,td{border:1px solid #d0d7de;padding:5px 8px;text-align:left;vertical-align:top}
        th{background:#f6f8fa;width:170px;font-weight:600}
        .status{font-size:28px;font-weight:700;margin:8px 0}
        .status-normal{color:#1a7f37}
        .status-caution{color:#b08800}
        .status-danger{color:#cf222e}
        .status-unknown{color:#6e7781}
        .finding{border:1px solid #d0d7de;border-left:4px solid #6e7781;padding:6px 10px;margin:8px 0}
        .finding-caution{border-left-color:#b08800}
        .finding-danger{border-left-color:#cf222e}
        .finding-title{font-weight:700}
        .footer{margin-top:24px;border-top:1px solid #d0d7de;padding-top:8px;color:#57606a}
        .action{--action-color:#6e7781;border:1px solid #d0d7de;border-left:5px solid var(--action-color);padding:8px 16px;margin:18px 0;page-break-inside:avoid}
        .action-immediate{--action-color:#cf222e}
        .action-soon{--action-color:#b08800}
        .action-routine{--action-color:#1a7f37}
        .action-checksetup{--action-color:#6e7781}
        .action h2{border:0;margin:0 0 8px;padding:0}
        .action-urgency{display:inline-block;background:var(--action-color);color:white;font-weight:700;padding:2px 8px;border-radius:3px}
        .action-soon .action-urgency{color:#111}
        .action-headline{font-size:18px;font-weight:700;margin:10px 0}
        .action ol{padding-left:24px;margin:8px 0}.action li{margin:8px 0}.next-check{font-weight:700;margin-top:12px}
        @media print{
            body{margin:0;font-size:11px}
            h2{page-break-after:avoid}
            table,.finding,.action{page-break-inside:avoid}
        }
        """;
}
