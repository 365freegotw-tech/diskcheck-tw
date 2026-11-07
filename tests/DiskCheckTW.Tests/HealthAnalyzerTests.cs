using Xunit;
using DiskCheckTW.Core.Models;
using DiskCheckTW.Core.Analysis;

namespace DiskCheckTW.Tests;

public class HealthAnalyzerTests
{
    // ---------- 測試資料輔助 ----------

    private static SmartAttribute Attr(
        byte id, ulong raw = 0, byte current = 100, byte? threshold = null, string name = "") =>
        new() { Id = id, Name = name.Length == 0 ? $"attr{id:X2}" : name, Current = current, Worst = current, Threshold = threshold, Raw = raw };

    private static PhysicalDiskInfo Hdd(params SmartAttribute[] attributes) =>
        new()
        {
            Index = 0,
            Model = "TEST HDD",
            Kind = DriveKind.Hdd,
            BusType = "SATA",
            AtaSmart = new AtaSmartData { Attributes = attributes },
        };

    private static PhysicalDiskInfo SataSsd(params SmartAttribute[] attributes) =>
        new()
        {
            Index = 1,
            Model = "TEST SATA SSD",
            Kind = DriveKind.SataSsd,
            BusType = "SATA",
            AtaSmart = new AtaSmartData { Attributes = attributes },
        };

    private static PhysicalDiskInfo Nvme(NvmeHealthLog log) =>
        new()
        {
            Index = 2,
            Model = "TEST NVME",
            Kind = DriveKind.NvmeSsd,
            BusType = "NVMe",
            NvmeHealth = log,
        };

    private static NvmeHealthLog NvmeLog(
        byte criticalWarning = 0, int kelvin = 300, byte spare = 90, byte spareThreshold = 10,
        byte used = 3, ulong mediaErrors = 0, ulong hours = 1234, ulong cycles = 42) =>
        new()
        {
            CriticalWarning = criticalWarning,
            TemperatureKelvin = kelvin,
            AvailableSpare = spare,
            AvailableSpareThreshold = spareThreshold,
            PercentageUsed = used,
            PowerOnHours = hours,
            PowerCycles = cycles,
            MediaErrors = mediaErrors,
        };

    /// <summary>一組完全正常的 HDD 屬性：無壞軌、溫度 40°C、有通電時間與開機次數。</summary>
    private static SmartAttribute[] CleanHddAttrs() =>
    [
        Attr(0x05, raw: 0),
        Attr(0x09, raw: 0x0000_0000_0000_0BB8),   // 3000 小時
        Attr(0x0C, raw: 0x0000_0000_0000_0064),   // 100 次
        Attr(0xC2, raw: 0x0000_002D_0014_0028),   // 小端排版 40°C
        Attr(0xC5, raw: 0),
        Attr(0xC6, raw: 0),
        Attr(0xC7, raw: 0),
    ];

    private static ScanResult Scan(int ok = 1000, int slow = 0, int error = 0, bool cancelled = false) =>
        new() { Mode = ScanMode.QuickSample, OkBlocks = ok, SlowBlocks = slow, ErrorBlocks = error, Cancelled = cancelled };

    private static bool HasTitle(Diagnosis d, FindingSeverity severity, string titlePart) =>
        d.Findings.Any(f => f.Severity == severity && f.Title.Contains(titlePart));

    // ---------- A. 資料不足 ----------

    [Fact]
    public void UsbEnclosureWithHealthUnavailableReason_IsInsufficientData()
    {
        var disk = new PhysicalDiskInfo
        {
            Index = 3,
            Kind = DriveKind.Hdd,
            BusType = "USB",
            HealthUnavailableReason = "USB 外接盒不支援 SMART 透傳",
        };

        var d = HealthAnalyzer.Analyze(disk);

        Assert.Equal(HealthStatus.InsufficientData, d.Status);
        Assert.Equal("資料不足", d.StatusText);
        Assert.Equal("目前無法取得足夠資料判斷健康狀態。", d.Summary);
        Assert.Contains("USB 外接盒不支援 SMART 透傳", d.Findings.Single().Detail);
    }

    [Fact]
    public void BothSmartSourcesMissing_IsInsufficientData()
    {
        var disk = new PhysicalDiskInfo { Index = 4, Kind = DriveKind.Unknown, BusType = "RAID" };

        var d = HealthAnalyzer.Analyze(disk);

        Assert.Equal(HealthStatus.InsufficientData, d.Status);
        Assert.Equal("資料不足", d.StatusText);
    }

    [Fact]
    public void EmptyAtaAttributeTable_IsInsufficientData()
    {
        var disk = new PhysicalDiskInfo
        {
            Index = 5,
            Kind = DriveKind.Hdd,
            AtaSmart = new AtaSmartData { Attributes = Array.Empty<SmartAttribute>() },
        };

        var d = HealthAnalyzer.Analyze(disk);

        Assert.Equal(HealthStatus.InsufficientData, d.Status);
    }

    [Fact]
    public void InsufficientDataWithScanErrors_IsDanger()
    {
        var disk = new PhysicalDiskInfo
        {
            Index = 6,
            Kind = DriveKind.Hdd,
            BusType = "USB",
            HealthUnavailableReason = "USB 外接盒不支援 SMART 透傳",
        };

        var d = HealthAnalyzer.Analyze(disk, Scan(ok: 500, error: 7));

        Assert.Equal(HealthStatus.Danger, d.Status);
        Assert.Equal("危險", d.StatusText);
        Assert.Contains("7", d.Findings.Single(f => f.Severity == FindingSeverity.Danger).Title);
    }

    [Fact]
    public void NvmeKindWithOnlyAtaSmart_IsInsufficientData()
    {
        var disk = new PhysicalDiskInfo
        {
            Index = 7,
            Kind = DriveKind.NvmeSsd,
            BusType = "NVMe",
            AtaSmart = new AtaSmartData { Attributes = CleanHddAttrs() },
        };

        var d = HealthAnalyzer.Analyze(disk);

        Assert.Equal(HealthStatus.InsufficientData, d.Status);
        Assert.Equal("資料不足", d.StatusText);
        Assert.True(HasTitle(d, FindingSeverity.Info, "硬碟類型與健康資料格式不符"));
    }

    // ---------- B. ATA ----------

    [Fact]
    public void HealthyHdd_IsNormal_SummaryDisclaims_AndLifePercentIsNull()
    {
        var d = HealthAnalyzer.Analyze(Hdd(CleanHddAttrs()));

        Assert.Equal(HealthStatus.Normal, d.Status);
        Assert.Equal("正常", d.StatusText);
        Assert.Contains("不保證", d.Summary);
        Assert.Null(d.LifeRemainingPercent);
        Assert.Equal(40, d.TemperatureCelsius);
        Assert.Equal(3000UL, d.PowerOnHours);
        Assert.Equal(100UL, d.PowerCycles);
    }

    [Fact]
    public void ReallocatedSectorsAboveZero_IsCaution()
    {
        var attrs = CleanHddAttrs();
        attrs[0] = Attr(0x05, raw: 8);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        Assert.Equal(HealthStatus.Caution, d.Status);
        Assert.Equal("注意", d.StatusText);
        Assert.True(HasTitle(d, FindingSeverity.Caution, "05"));
        Assert.Contains("8", d.Findings.Single(f => f.Severity == FindingSeverity.Caution).Title);
    }

    [Fact]
    public void ReallocatedSectorsDetailReportsTheRealCount()
    {
        var attrs = CleanHddAttrs();
        attrs[0] = Attr(0x05, raw: 8);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        var finding = d.Findings.Single(f => f.Severity == FindingSeverity.Caution);
        Assert.Contains("8 個", finding.Detail);
    }

    [Fact]
    public void PendingSectorsAboveZero_IsCaution()
    {
        var attrs = CleanHddAttrs();
        attrs[4] = Attr(0xC5, raw: 3);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        Assert.Equal(HealthStatus.Caution, d.Status);
        Assert.True(HasTitle(d, FindingSeverity.Caution, "C5"));
    }

    [Fact]
    public void CurrentAtOrBelowVendorThreshold_IsDanger()
    {
        var attrs = CleanHddAttrs();
        attrs[0] = Attr(0x05, raw: 0, current: 10, threshold: 10);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        Assert.Equal(HealthStatus.Danger, d.Status);
        Assert.Equal("危險", d.StatusText);
        Assert.True(HasTitle(d, FindingSeverity.Danger, "05"));
    }

    [Fact]
    public void SmartReturnStatusFailing_IsDanger()
    {
        var disk = new PhysicalDiskInfo
        {
            Index = 0,
            Kind = DriveKind.Hdd,
            AtaSmart = new AtaSmartData { Attributes = CleanHddAttrs(), SmartReturnStatusFailing = true },
        };

        var d = HealthAnalyzer.Analyze(disk);

        Assert.Equal(HealthStatus.Danger, d.Status);
        Assert.Contains("FAILING", d.Findings.Single(f => f.Severity == FindingSeverity.Danger).Title);
    }

    [Fact]
    public void UltraDmaCrcErrorsAloneStayNormalAndProduceInfo()
    {
        var attrs = CleanHddAttrs();
        attrs[6] = Attr(0xC7, raw: 12);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        Assert.Equal(HealthStatus.Normal, d.Status);
        Assert.Equal("正常", d.StatusText);
        Assert.True(HasTitle(d, FindingSeverity.Info, "C7"));
        Assert.DoesNotContain(d.Findings, f => f.Severity != FindingSeverity.Info);
    }

    [Fact]
    public void UltraDmaCrcTitleUsesUpperCaseAttributeId()
    {
        var attrs = CleanHddAttrs();
        attrs[6] = Attr(0xC7, raw: 12);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        var finding = d.Findings.Single(f => f.Severity == FindingSeverity.Info);
        Assert.Contains("C7", finding.Title);
        Assert.Contains("12", finding.Title);
    }

    [Fact]
    public void HddTemperature56_IsCaution()
    {
        var attrs = CleanHddAttrs();
        attrs[3] = Attr(0xC2, raw: 56);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        Assert.Equal(HealthStatus.Caution, d.Status);
        Assert.Equal(56, d.TemperatureCelsius);
        Assert.True(HasTitle(d, FindingSeverity.Caution, "56"));
    }

    [Fact]
    public void HddTemperatureBelowCaution_IsNormal()
    {
        var attrs = CleanHddAttrs();
        attrs[3] = Attr(0xC2, raw: 54);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        Assert.Equal(HealthStatus.Normal, d.Status);
        Assert.Equal(54, d.TemperatureCelsius);
    }

    [Fact]
    public void TemperatureIsLowByteOfC2Raw()
    {
        var attrs = CleanHddAttrs();
        attrs[3] = Attr(0xC2, raw: 0x0000_002D_0014_0028);

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        Assert.Equal(40, d.TemperatureCelsius);
    }

    [Fact]
    public void TemperatureFallsBackToAttributeBE()
    {
        var attrs = CleanHddAttrs();
        attrs[3] = Attr(0xC2, raw: 0);          // 低位元組不可採信
        attrs[6] = Attr(0xBE, raw: 0x32);       // BE（Airflow Temperature）50°C

        var d = HealthAnalyzer.Analyze(Hdd(attrs));

        Assert.Equal(50, d.TemperatureCelsius);
    }

    [Fact]
    public void MissingAttributesLeaveTemperaturePowerOnHoursAndCyclesNull()
    {
        var d = HealthAnalyzer.Analyze(Hdd(Attr(0x05, raw: 0)));

        Assert.Null(d.TemperatureCelsius);
        Assert.Null(d.PowerOnHours);
        Assert.Null(d.PowerCycles);
    }

    [Fact]
    public void SataSsdWithE7Life5_IsCautionAndLifeIs5()
    {
        var d = HealthAnalyzer.Analyze(SataSsd(
            Attr(0x05, raw: 0),
            Attr(0xE7, current: 5),
            Attr(0xC2, raw: 34)));

        Assert.Equal(HealthStatus.Caution, d.Status);
        Assert.Equal(5, d.LifeRemainingPercent);
        Assert.True(HasTitle(d, FindingSeverity.Caution, "5"));
    }

    [Fact]
    public void SataSsdWithoutE7_LifeRemainsNull()
    {
        var d = HealthAnalyzer.Analyze(SataSsd(
            Attr(0x05, raw: 0),
            Attr(0xE8, current: 3),
            Attr(0xC2, raw: 34)));

        Assert.Null(d.LifeRemainingPercent);
        Assert.Equal(HealthStatus.Normal, d.Status);
    }

    [Fact]
    public void SataSsdTemperatureUsesSsdThresholdNotHddThreshold()
    {
        var d = HealthAnalyzer.Analyze(SataSsd(Attr(0x05, raw: 0), Attr(0xC2, raw: 62)));

        Assert.Equal(HealthStatus.Normal, d.Status);
        Assert.Equal(62, d.TemperatureCelsius);
    }

    // ---------- C. NVMe ----------

    [Fact]
    public void HealthyNvme_IsNormalAndLifeIs100MinusUsed()
    {
        var d = HealthAnalyzer.Analyze(Nvme(NvmeLog(used: 3)));

        Assert.Equal(HealthStatus.Normal, d.Status);
        Assert.Equal(97, d.LifeRemainingPercent);
        Assert.Equal(27, d.TemperatureCelsius);
        Assert.Equal(1234UL, d.PowerOnHours);
        Assert.Equal(42UL, d.PowerCycles);
    }

    [Fact]
    public void NvmeCriticalWarningBit2_IsDanger()
    {
        var d = HealthAnalyzer.Analyze(Nvme(NvmeLog(criticalWarning: 0x04)));

        Assert.Equal(HealthStatus.Danger, d.Status);
        Assert.Contains("可靠度", d.Findings.Single(f => f.Severity == FindingSeverity.Danger).Title);
    }

    [Fact]
    public void NvmeCriticalWarningMultipleBits_ProduceOneFindingEach()
    {
        var d = HealthAnalyzer.Analyze(Nvme(NvmeLog(criticalWarning: 0x09, used: 10)));   // bit0 + bit3

        Assert.Equal(HealthStatus.Danger, d.Status);
        Assert.Equal(2, d.Findings.Count(f => f.Severity == FindingSeverity.Danger));
        Assert.Contains("備用空間", string.Join("|", d.Findings.Select(f => f.Title)));
        Assert.Contains("唯讀", string.Join("|", d.Findings.Select(f => f.Title)));
    }

    [Fact]
    public void NvmeMediaErrors_IsDanger()
    {
        var d = HealthAnalyzer.Analyze(Nvme(NvmeLog(mediaErrors: 4)));

        Assert.Equal(HealthStatus.Danger, d.Status);
        Assert.Contains("Media and Data Integrity Errors", d.Findings.Single(f => f.Severity == FindingSeverity.Danger).Detail);
    }

    [Fact]
    public void NvmeAvailableSpareBelowThreshold_IsDanger()
    {
        var d = HealthAnalyzer.Analyze(Nvme(NvmeLog(spare: 8, spareThreshold: 10, used: 20)));

        Assert.Equal(HealthStatus.Danger, d.Status);
    }

    [Fact]
    public void NvmePercentageUsed95_IsCaution()
    {
        var d = HealthAnalyzer.Analyze(Nvme(NvmeLog(used: 95)));

        Assert.Equal(HealthStatus.Caution, d.Status);
        Assert.Equal(5, d.LifeRemainingPercent);
        Assert.True(HasTitle(d, FindingSeverity.Caution, "95"));
    }

    [Fact]
    public void NvmeTemperature71_IsCaution()
    {
        var d = HealthAnalyzer.Analyze(Nvme(NvmeLog(kelvin: 344)));   // 71°C

        Assert.Equal(HealthStatus.Caution, d.Status);
        Assert.Equal(71, d.TemperatureCelsius);
    }

    // ---------- D. 掃描 ----------

    [Fact]
    public void ScanErrorBlocks_IsDanger()
    {
        var d = HealthAnalyzer.Analyze(Hdd(CleanHddAttrs()), Scan(ok: 900, error: 2));

        Assert.Equal(HealthStatus.Danger, d.Status);
        Assert.Contains("2", d.Findings.First().Title);
    }

    [Fact]
    public void ScanSlowRatioAboveThreshold_IsCaution()
    {
        var d = HealthAnalyzer.Analyze(Hdd(CleanHddAttrs()), Scan(ok: 900, slow: 90));

        Assert.Equal(HealthStatus.Caution, d.Status);
    }

    [Fact]
    public void ScanSlowRatioWithinThreshold_IsNormal()
    {
        var d = HealthAnalyzer.Analyze(Hdd(CleanHddAttrs()), Scan(ok: 990, slow: 10));

        Assert.Equal(HealthStatus.Normal, d.Status);
    }

    [Fact]
    public void ScanCancelled_AddsInfoOnly()
    {
        var d = HealthAnalyzer.Analyze(Hdd(CleanHddAttrs()), Scan(ok: 300, cancelled: true));

        Assert.Equal(HealthStatus.Normal, d.Status);
        Assert.True(HasTitle(d, FindingSeverity.Info, "掃描未完成"));
    }

    // ---------- E. 彙總 ----------

    [Fact]
    public void FindingsAreSortedWithDangerFirst()
    {
        var attrs = CleanHddAttrs();
        attrs[0] = Attr(0x05, raw: 4);
        attrs[6] = Attr(0xC7, raw: 9);

        var d = HealthAnalyzer.Analyze(Hdd(attrs), Scan(ok: 900, error: 1));

        Assert.Equal(FindingSeverity.Danger, d.Findings[0].Severity);
        Assert.Equal(FindingSeverity.Info, d.Findings[^1].Severity);
        Assert.Equal("存在重要健康警告或明確讀取錯誤，建議優先備份並評估更換硬碟。", d.Summary);
    }

    [Fact]
    public void EveryFindingHasTitleDetailImpactAndAdvice()
    {
        var attrs = CleanHddAttrs();
        attrs[0] = Attr(0x05, raw: 2, current: 97, threshold: 90);
        attrs[4] = Attr(0xC5, raw: 1);
        attrs[6] = Attr(0xC7, raw: 3);

        var d = HealthAnalyzer.Analyze(Hdd(attrs), Scan(ok: 800, slow: 50, error: 1, cancelled: true));

        Assert.True(d.Findings.Count >= 5);
        Assert.All(d.Findings, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Title));
            Assert.False(string.IsNullOrWhiteSpace(f.Detail));
            Assert.False(string.IsNullOrWhiteSpace(f.Impact));
            Assert.False(string.IsNullOrWhiteSpace(f.Advice));
        });
    }
}
