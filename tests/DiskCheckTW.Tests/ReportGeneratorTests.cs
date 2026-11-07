namespace DiskCheckTW.Tests;

using Xunit;
using System.Text.RegularExpressions;
using DiskCheckTW.Core;
using DiskCheckTW.Core.Models;
using DiskCheckTW.Core.Reporting;

public class ReportGeneratorTests
{
    private const string Serial = "WD-ABC12345";

    private static PhysicalDiskInfo Disk(
        DriveKind kind = DriveKind.Hdd,
        string model = "WDC WD10EZEX",
        string? serial = Serial,
        NvmeHealthLog? nvme = null) => new()
        {
            Index = 1,
            Model = model,
            Vendor = "WDC",
            Serial = serial,
            Firmware = "01.01A01",
            SizeBytes = 1_000_000_000_000,
            LogicalSectorSize = 512,
            Kind = kind,
            BusType = kind == DriveKind.NvmeSsd ? "NVMe" : "SATA",
            DriveLetters = new[] { "C:" },
            IsSystemDisk = true,
            NvmeHealth = nvme,
        };

    private static Diagnosis Diag(HealthStatus status = HealthStatus.Normal, string statusText = "正常") => new()
    {
        Status = status,
        StatusText = statusText,
        Summary = "測試摘要",
        TemperatureCelsius = 30,
        PowerOnHours = 1234UL,
        PowerCycles = 42UL,
    };

    private static ReportContext Ctx(bool maskSerial = true) => new()
    {
        GeneratedAt = new DateTime(2024, 1, 2, 3, 4, 5),
        OsDescription = "Microsoft Windows 11 家用中文版",
        AppVersion = "1.0.0",
        MaskSerial = maskSerial,
    };

    private static string Html(
        PhysicalDiskInfo disk,
        Diagnosis? diagnosis = null,
        ScanResult? scan = null,
        ReportContext? context = null) =>
        ReportGenerator.GenerateHtml(disk, diagnosis ?? Diag(), scan, context ?? Ctx());

    [Fact]
    public void MaskSerial_保留前四碼其餘遮罩()
    {
        Assert.Equal("WD-A*******", ReportGenerator.MaskSerial(Serial));
    }

    [Fact]
    public void MaskSerial_沒有序號時回報無法取得()
    {
        Assert.Equal("無法取得", ReportGenerator.MaskSerial(null));
        Assert.Equal("無法取得", ReportGenerator.MaskSerial(""));
        Assert.Equal("無法取得", ReportGenerator.MaskSerial("   "));
    }

    [Fact]
    public void MaskSerial_四碼以內全部遮罩()
    {
        Assert.Equal("**", ReportGenerator.MaskSerial("AB"));
        Assert.Equal("****", ReportGenerator.MaskSerial("ABCD"));
    }

    [Fact]
    public void 預設報告不含完整序號只含遮罩序號()
    {
        var html = Html(Disk());

        Assert.DoesNotContain(Serial, html);
        Assert.Contains("WD-A*******", html);
    }

    [Fact]
    public void MaskSerial關閉時報告含完整序號()
    {
        var html = Html(Disk(), context: Ctx(maskSerial: false));

        Assert.Contains(Serial, html);
        Assert.DoesNotContain("WD-A*******", html);
    }

    [Fact]
    public void 型號中的script標籤被HTML編碼()
    {
        var html = Html(Disk(model: "<script>x"));

        Assert.Contains("&lt;script&gt;x", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void 未掃描時報告顯示未執行唯讀掃描()
    {
        var html = Html(Disk(), scan: null);

        Assert.Contains("本次未執行唯讀掃描", html);
    }

    [Fact]
    public void 使用者停止時報告顯示明確中止原因()
    {
        var html = Html(Disk(), scan: new ScanResult { Cancelled = true });
        Assert.Contains("<th>中止原因</th><td>使用者手動停止</td>", html);
    }

    [Fact]
    public void 有掃描結果時顯示掃描資料與錯誤位移()
    {
        var scan = new ScanResult
        {
            Mode = ScanMode.Full,
            StartedAt = new DateTime(2024, 1, 2, 3, 4, 5),
            Elapsed = TimeSpan.FromMinutes(75),
            BytesScanned = 500_000_000,
            BlocksTotal = 100,
            OkBlocks = 98,
            SlowBlocks = 1,
            ErrorBlocks = 1,
            AvgLatencyMs = 12.5,
            MaxLatencyMs = 88.25,
        };
        scan.ErrorOffsets.Add(4096);

        var html = Html(Disk(), scan: scan);

        Assert.Contains("完整掃描", html);
        Assert.Contains("01:15:00", html);
        Assert.Contains("錯誤區塊位移", html);
        Assert.Contains("4096", html);
        Assert.DoesNotContain("本次未執行唯讀掃描", html);
    }

    [Fact]
    public void Danger狀態使用危險樣式()
    {
        var html = Html(Disk(), diagnosis: Diag(HealthStatus.Danger, "危險"));

        Assert.Contains("status-danger", html);
        Assert.Contains("危險", html);
        Assert.Contains("action-immediate", html);
        Assert.Contains("這顆硬碟有故障風險，請先備份資料，並準備更換硬碟。", html);
        Assert.Contains("<li><strong>立刻備份重要資料</strong>", html);
    }

    [Fact]
    public void Routine報告使用產生時間計算下次檢測日()
    {
        var html = Html(Disk(), diagnosis: Diag(HealthStatus.Normal, "正常"));
        Assert.Contains("action-routine", html);
        Assert.Contains("下次建議檢測日：2024-04-02", html);
    }

    [Fact]
    public void NVMe報告含已使用百分比()
    {
        var nvme = new NvmeHealthLog
        {
            CriticalWarning = 0,
            TemperatureKelvin = 313,
            AvailableSpare = 90,
            AvailableSpareThreshold = 10,
            PercentageUsed = 120,
            PowerOnHours = 100UL,
            MediaErrors = 0UL,
        };

        var html = Html(Disk(DriveKind.NvmeSsd, "Samsung 990 PRO", "S4ZXNG0T123456", nvme));

        Assert.Contains("已使用百分比", html);
        Assert.Contains("120 %", html);
        Assert.Contains("NVMe SSD", html);
    }

    [Fact]
    public void 輸出為離線HTML只有官方網址()
    {
        var html = Html(Disk());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Equal(new[] { OfficialLinks.OfficialSite },
            Regex.Matches(html, @"https?://[^""\s<>]+")
                .Select(m => m.Value).ToArray());
    }
}
