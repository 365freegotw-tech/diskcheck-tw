using DiskCheckTW.Core;
using DiskCheckTW.Core.Models;

namespace DiskCheckTW.Core.Analysis;

/// <summary>「建議您接下來這樣做」的門檻。UI、報告與建議都讀同一組值，調整門檻只改這裡。</summary>
public static class ActionRules
{
    public const int FullCheckIntervalMonths = 3;
    /// <summary>傳統硬碟通電時數超過此值（約 4 年），即使目前正常也建議開始規劃更換。</summary>
    public const int HddOldHours = 35000;

    /// <summary>SSD 剩餘壽命低於此值，要開始規劃更換時間。</summary>
    public const int SsdLifePlanSoon = 30;

    /// <summary>SSD 剩餘壽命低於此值，3 個月內要換掉。</summary>
    public const int SsdLifeReplaceSoon = 10;
}

/// <summary>把 HealthAnalyzer 算好的 Diagnosis 翻成白話的下一步行動。純邏輯，不做 I/O。</summary>
public static class ActionAdvisor
{
    // 市面常見容量（GB，1GB = 1e9 bytes），由小到大；建議取第一個不小於目前容量的值。
    private static readonly int[] NewDiskSizesGb =
        { 120, 240, 256, 480, 500, 512, 960, 1000, 2000, 4000, 8000, 12000, 16000, 20000 };

    public static ActionPlan Build(PhysicalDiskInfo disk, Diagnosis diagnosis, ScanResult? scan = null) =>
        diagnosis.Status switch
        {
            HealthStatus.Danger => Danger(disk, diagnosis, scan),
            HealthStatus.Caution => Caution(disk, diagnosis, scan),
            HealthStatus.Normal => Normal(disk, diagnosis, scan),
            _ => InsufficientData(disk, diagnosis, scan),
        };

    private static ActionPlan Danger(PhysicalDiskInfo disk, Diagnosis diagnosis, ScanResult? scan)
    {
        var steps = new List<ActionStep>
        {
            Step(S.T("A001"),
                S.F("A002", Letters(disk))),
            Step(S.T("A003"),
                S.T("A004")),
            Step(S.T("A005"),
                S.F("A006", CapacityAdvice(disk))
                + (disk.Kind == DriveKind.Hdd && disk.IsSystemDisk ? S.T("A007") : "")),
        };

        if (disk.IsSystemDisk)
            steps.Add(Step(S.T("A008"),
                S.T("A009")));

        if (scan is not null && scan.ErrorBlocks > 0)
            steps.Add(Step(S.T("A010"),
                S.F("A011", scan.ErrorBlocks)));

        if (disk.NvmeHealth is not null && (disk.NvmeHealth.CriticalWarning & 0x08) != 0)
            steps.Insert(0, Step(S.T("A012"),
                S.T("A013")));

        return Plan(ActionUrgency.Immediate, S.T("A014"),
            S.T("A015"), steps);
    }

    private static ActionPlan Caution(PhysicalDiskInfo disk, Diagnosis diagnosis, ScanResult? scan)
    {
        var steps = new List<ActionStep>
        {
            Step(S.T("A016"),
                S.F("A017", Letters(disk))),
        };

        if (HasFinding(diagnosis, f => f.Code == "TEMP_HIGH"))
            steps.Add(Step(S.T("A018"),
                S.T("A019")));

        bool lifeAlmostGone =
            (diagnosis.LifeRemainingPercent is int life && life <= ActionRules.SsdLifeReplaceSoon)
            || (disk.NvmeHealth is not null && disk.NvmeHealth.PercentageUsed >= HealthRules.NvmeUsedCaution);
        if (lifeAlmostGone)
            steps.Add(Step(S.T("A020"),
                S.T("A021")));

        if (HasFinding(diagnosis, f => f.Severity == FindingSeverity.Caution
                && (f.Code == "ATA_05"
                 || f.Code == "ATA_C5"
                 || f.Code == "ATA_C6")))
            steps.Add(Step(S.T("A022"),
                S.F("A023", CapacityAdvice(disk))));

        steps.Add(Step(S.T("A024"),
            S.T("A025")));

        return Plan(ActionUrgency.Soon, S.T("A026"),
            S.T("A027"), steps);
    }

    private static ActionPlan Normal(PhysicalDiskInfo disk, Diagnosis diagnosis, ScanResult? scan)
    {
        var steps = new List<ActionStep>
        {
            Step(S.T("A028"),
                S.T("A029")),
            Step(S.T("A030"),
                S.F("A031", ActionRules.FullCheckIntervalMonths)),
        };

        if (HasFinding(diagnosis, f => f.Severity == FindingSeverity.Info && f.Code == "ATA_C7"))
            steps.Add(Step(S.T("A032"),
                S.T("A033")));

        if (disk.Kind == DriveKind.Hdd && diagnosis.PowerOnHours is ulong hours && hours >= ActionRules.HddOldHours)
            steps.Add(Step(S.T("A034"),
                S.T("A035")));

        if (diagnosis.LifeRemainingPercent is int life && life <= ActionRules.SsdLifePlanSoon)
            steps.Add(Step(S.T("A036"),
                S.F("A037", life)));

        if (scan is not null && scan.Cancelled)
            steps.Add(Step(S.T("A038"),
                S.T("A039")));

        return Plan(ActionUrgency.Routine, S.T("A040"),
            S.T("A041"), steps);
    }

    private static ActionPlan InsufficientData(PhysicalDiskInfo disk, Diagnosis diagnosis, ScanResult? scan)
    {
        var steps = new List<ActionStep>
        {
            Step(S.T("A042"),
                S.T("A043")),
        };

        if (disk.BusType == "USB")
            steps.Add(Step(S.T("A044"),
                S.T("A045")));
        else if (disk.BusType == "RAID")
            steps.Add(Step(S.T("A046"),
                S.T("A047")));

        steps.Add(Step(S.T("A048"),
            S.T("A049")));

        if (scan is not null && scan.ErrorBlocks == 0 && !scan.Cancelled)
            steps.Add(Step(S.T("A050"),
                S.T("A051")));

        return Plan(ActionUrgency.CheckSetup, S.T("A052"),
            S.T("A053"), steps);
    }

    private static string Letters(PhysicalDiskInfo disk) =>
        disk.DriveLetters.Count == 0 ? S.T("A054") : string.Join(S.ListSep, disk.DriveLetters);

    private static string CapacityAdvice(PhysicalDiskInfo disk)
    {
        if (disk.SizeBytes == 0) return S.T("A055");

        double gb = disk.SizeBytes / 1_000_000_000d;
        foreach (int size in NewDiskSizesGb)
            if (size >= gb) return S.F("A056", size);
        return S.F("A057", gb);
    }

    private static bool HasFinding(Diagnosis diagnosis, Func<Finding, bool> match) =>
        diagnosis.Findings.Any(match);

    private static ActionStep Step(string title, string detail) => new() { Title = title, Detail = detail };

    private static ActionPlan Plan(ActionUrgency urgency, string urgencyText, string headline, List<ActionStep> steps) =>
        new() { Urgency = urgency, UrgencyText = urgencyText, Headline = headline, Steps = steps };
}
