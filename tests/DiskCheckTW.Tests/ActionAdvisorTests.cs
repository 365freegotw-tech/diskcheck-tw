using Xunit;
using DiskCheckTW.Core.Models;
using DiskCheckTW.Core.Analysis;

namespace DiskCheckTW.Tests;

public class ActionAdvisorTests
{
    private static PhysicalDiskInfo Disk(
        DriveKind kind = DriveKind.Hdd,
        long sizeBytes = 1_000_204_886_016,
        bool system = false,
        string bus = "SATA",
        string[]? letters = null,
        NvmeHealthLog? nvme = null) => new()
        {
            Index = 0,
            Model = "TEST DISK",
            SizeBytes = sizeBytes,
            Kind = kind,
            BusType = bus,
            IsSystemDisk = system,
            DriveLetters = letters ?? new[] { "C:", "D:" },
            NvmeHealth = nvme,
        };

    private static Diagnosis Diag(
        HealthStatus status,
        FindingSeverity severity = FindingSeverity.Caution,
        string title = "",
        ulong? hours = null,
        int? life = null,
        string code = "") => new()
        {
            Status = status,
            StatusText = status.ToString(),
            Findings = title.Length == 0
                ? Array.Empty<Finding>()
                : new[] { new Finding { Severity = severity, Code = code, Title = title, Detail = "d", Impact = "i", Advice = "a" } },
            PowerOnHours = hours,
            LifeRemainingPercent = life,
        };

    private static ActionStep StepOf(ActionPlan plan, string title) =>
        Assert.Single(plan.Steps, s => s.Title == title);

    [Fact]
    public void Danger_urgency_and_first_backup_step()
    {
        var plan = ActionAdvisor.Build(Disk(), Diag(HealthStatus.Danger));

        Assert.Equal(ActionUrgency.Immediate, plan.Urgency);
        Assert.Equal("立即處理", plan.UrgencyText);
        Assert.Equal("這顆硬碟有故障風險，請先備份資料，並準備更換硬碟。", plan.Headline);
        Assert.Contains("備份", plan.Steps[0].Title);
        Assert.Contains("C:、D:", plan.Steps[0].Detail);
    }

    [Fact]
    public void Danger_system_disk_adds_windows_step_only_for_system_disk()
    {
        const string title = "這是系統碟，需要搬移或重灌 Windows";

        var system = ActionAdvisor.Build(Disk(system: true), Diag(HealthStatus.Danger));
        var data = ActionAdvisor.Build(Disk(system: false), Diag(HealthStatus.Danger));

        Assert.Contains(system.Steps, s => s.Title == title);
        Assert.DoesNotContain(data.Steps, s => s.Title == title);
    }

    [Fact]
    public void Danger_hdd_system_disk_recommends_ssd_replacement()
    {
        var plan = ActionAdvisor.Build(Disk(DriveKind.Hdd, system: true), Diag(HealthStatus.Danger));

        Assert.Contains("SSD", StepOf(plan, "準備更換新硬碟").Detail);
    }

    [Fact]
    public void Danger_hdd_data_disk_does_not_mention_ssd()
    {
        var plan = ActionAdvisor.Build(Disk(DriveKind.Hdd, system: false), Diag(HealthStatus.Danger));

        Assert.DoesNotContain("SSD", StepOf(plan, "準備更換新硬碟").Detail);
    }

    [Fact]
    public void Capacity_advice_rounds_up_to_common_sizes()
    {
        var twoTb = ActionAdvisor.Build(Disk(sizeBytes: 1_000_204_886_016), Diag(HealthStatus.Danger));
        var oneTwenty = ActionAdvisor.Build(Disk(sizeBytes: 120_034_123_776), Diag(HealthStatus.Danger));

        Assert.Contains("至少 2000 GB", StepOf(twoTb, "準備更換新硬碟").Detail);
        Assert.Contains("至少 240 GB", StepOf(oneTwenty, "準備更換新硬碟").Detail);
    }

    [Fact]
    public void Capacity_advice_without_size_uses_same_or_larger()
    {
        var plan = ActionAdvisor.Build(Disk(sizeBytes: 0), Diag(HealthStatus.Danger));

        Assert.Contains("與原本相同或更大", StepOf(plan, "準備更換新硬碟").Detail);
    }

    [Fact]
    public void Danger_with_scan_errors_adds_file_check_step()
    {
        var scan = new ScanResult { Mode = ScanMode.Full, BlocksTotal = 100, ErrorBlocks = 3 };
        var plan = ActionAdvisor.Build(Disk(), Diag(HealthStatus.Danger), scan);

        Assert.Contains(plan.Steps, s => s.Title == "確認重要檔案能正常開啟");
    }

    [Fact]
    public void Danger_nvme_readonly_mode_is_first_step()
    {
        var nvme = new NvmeHealthLog { CriticalWarning = 0x08, TemperatureKelvin = 313 };
        var plan = ActionAdvisor.Build(Disk(DriveKind.NvmeSsd, nvme: nvme), Diag(HealthStatus.Danger));

        Assert.Equal("硬碟已進入唯讀模式", plan.Steps[0].Title);
    }

    [Fact]
    public void Caution_temperature_finding_adds_cooling_step()
    {
        var plan = ActionAdvisor.Build(Disk(), Diag(HealthStatus.Caution, FindingSeverity.Caution, "溫度過高（62°C）", code: "TEMP_HIGH"));

        Assert.Contains(plan.Steps, s => s.Title == "改善散熱");
    }

    [Fact]
    public void Caution_reallocated_sectors_adds_replacement_and_ends_with_recheck()
    {
        var plan = ActionAdvisor.Build(
            Disk(), Diag(HealthStatus.Caution, FindingSeverity.Caution, "重新分配磁區 (05) 有 8 個", code: "ATA_05"));

        Assert.Contains(plan.Steps, s => s.Title == "準備更換硬碟");
        Assert.Equal("一個月後再檢測一次", plan.Steps[^1].Title);
    }

    [Fact]
    public void Normal_c7_info_adds_sata_cable_step()
    {
        var plan = ActionAdvisor.Build(
            Disk(), Diag(HealthStatus.Normal, FindingSeverity.Info, "UltraDMA CRC 錯誤 (C7) 有 12 次", code: "ATA_C7"));

        Assert.Contains(plan.Steps, s => s.Title == "檢查 SATA 傳輸線");
    }

    [Fact]
    public void Normal_old_hdd_plan_replacement_but_ssd_does_not()
    {
        var hdd = ActionAdvisor.Build(Disk(DriveKind.Hdd), Diag(HealthStatus.Normal, hours: 41439));
        var ssd = ActionAdvisor.Build(Disk(DriveKind.SataSsd), Diag(HealthStatus.Normal, hours: 41439));

        Assert.Contains(hdd.Steps, s => s.Title == "開始規劃更換");
        Assert.DoesNotContain(ssd.Steps, s => s.Title == "開始規劃更換");
    }

    [Fact]
    public void Normal_low_ssd_life_adds_life_step_with_percent()
    {
        var plan = ActionAdvisor.Build(Disk(DriveKind.SataSsd), Diag(HealthStatus.Normal, life: 25));

        Assert.Contains("25%", StepOf(plan, "留意 SSD 剩餘壽命").Detail);
    }

    [Fact]
    public void Normal_full_check_uses_three_month_interval()
    {
        var plan = ActionAdvisor.Build(Disk(), Diag(HealthStatus.Normal));
        Assert.Equal(3, ActionRules.FullCheckIntervalMonths);
        Assert.Equal("定期做一次完整檢測", plan.Steps[1].Title);
        Assert.Equal("建議每 3 個月執行一次 SMART 檢查加完整唯讀掃描，並把報告存起來，下次比較數值有沒有變化。", plan.Steps[1].Detail);
    }

    [Fact]
    public void InsufficientData_usb_and_raid_steps()
    {
        var usb = ActionAdvisor.Build(Disk(bus: "USB"), Diag(HealthStatus.InsufficientData));
        var raid = ActionAdvisor.Build(Disk(bus: "RAID"), Diag(HealthStatus.InsufficientData));

        Assert.Equal("先確認連接方式", usb.UrgencyText);
        Assert.Contains(usb.Steps, s => s.Title == "改接電腦內部，或更換外接盒");
        Assert.Contains(raid.Steps, s => s.Title == "使用 RAID 控制器的管理工具");
        Assert.DoesNotContain(raid.Steps, s => s.Title == "改接電腦內部，或更換外接盒");
    }

    [Fact]
    public void InsufficientData_finished_clean_scan_adds_reference_step()
    {
        var scan = new ScanResult { Mode = ScanMode.Full, BlocksTotal = 100, OkBlocks = 100 };
        var plan = ActionAdvisor.Build(Disk(bus: "USB"), Diag(HealthStatus.InsufficientData), scan);

        Assert.Contains(plan.Steps, s => s.Title == "唯讀掃描未發現讀取錯誤");
    }

    [Fact]
    public void Normal_cancelled_scan_adds_rerun_step()
    {
        var scan = new ScanResult { Mode = ScanMode.Full, BlocksTotal = 100, OkBlocks = 40, Cancelled = true };
        var plan = ActionAdvisor.Build(Disk(), Diag(HealthStatus.Normal), scan);

        Assert.Contains(plan.Steps, s => s.Title == "有空時跑完完整掃描");
    }

    [Fact]
    public void Every_status_has_steps_with_non_empty_details()
    {
        var scan = new ScanResult { Mode = ScanMode.Full, BlocksTotal = 100, ErrorBlocks = 2, Cancelled = true };
        var plans = new[]
        {
            ActionAdvisor.Build(Disk(), Diag(HealthStatus.Danger), scan),
            ActionAdvisor.Build(Disk(DriveKind.NvmeSsd, system: true, nvme: new NvmeHealthLog { CriticalWarning = 0x08 }), Diag(HealthStatus.Danger), scan),
            ActionAdvisor.Build(Disk(DriveKind.SataSsd, letters: Array.Empty<string>()), Diag(HealthStatus.Caution, FindingSeverity.Caution, "溫度過高（72°C）", life: 5), scan),
            ActionAdvisor.Build(Disk(DriveKind.SataSsd), Diag(HealthStatus.Normal, FindingSeverity.Info, "UltraDMA CRC 錯誤 (C7) 有 1 次", 40000, 20), scan),
            ActionAdvisor.Build(Disk(bus: "RAID"), Diag(HealthStatus.InsufficientData), scan),
        };

        foreach (var plan in plans)
        {
            Assert.NotEmpty(plan.Steps);
            Assert.False(string.IsNullOrEmpty(plan.Headline));
            Assert.False(string.IsNullOrEmpty(plan.UrgencyText));
            foreach (var step in plan.Steps)
            {
                Assert.False(string.IsNullOrEmpty(step.Title));
                Assert.False(string.IsNullOrEmpty(step.Detail));
            }
        }
    }
}
