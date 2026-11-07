using DiskCheckTW.Core;
using DiskCheckTW.Core.Models;

namespace DiskCheckTW.Core.Analysis;

/// <summary>健康診斷門檻。UI、報告與診斷都讀同一組值，調整門檻只改這裡。</summary>
public static class HealthRules
{
    public const int HddTempCaution = 55;
    public const int SsdTempCaution = 70;
    public const int NvmeTempCaution = 70;
    public const int NvmeUsedCaution = 90;
    public const int SsdLifeCaution = 10;
    public const double SlowBlockCautionRatio = 0.01;

    /// <summary>SMART 溫度屬性可採信的範圍，超出視為廠商自訂編碼。</summary>
    public const int TempMin = 1;
    public const int TempMax = 119;
}

/// <summary>純邏輯診斷：讀 PhysicalDiskInfo / ScanResult，產出 Diagnosis。不做 I/O、不呼叫 Windows API。</summary>
public static class HealthAnalyzer
{
    public static Diagnosis Analyze(PhysicalDiskInfo disk, ScanResult? scan = null)
    {
        var findings = new List<Finding>();
        var ata = disk.AtaSmart;
        var nvme = disk.NvmeHealth;

        bool insufficient = disk.HealthUnavailableReason is not null
            || (ata is null && nvme is null)
            || (ata is not null && ata.Attributes.Count == 0);

        int? temp = null;
        ulong? powerOnHours = null;
        ulong? powerCycles = null;
        int? life = null;

        if (insufficient)
        {
            findings.Add(Find(FindingSeverity.Info, "HEALTH_UNAVAILABLE", S.T("H001"),
                disk.HealthUnavailableReason is null
                    ? S.T("H002")
                    : S.F("H003", disk.HealthUnavailableReason),
                S.T("H004"),
                S.T("H005")));
        }
        else if (ata is not null && disk.Kind is DriveKind.Hdd or DriveKind.SataSsd or DriveKind.Unknown)
        {
            AnalyzeAta(disk, ata, findings, out temp, out powerOnHours, out powerCycles, out life);
        }
        else if (nvme is not null && disk.Kind == DriveKind.NvmeSsd)
        {
            AnalyzeNvme(nvme, findings, out temp, out powerOnHours, out powerCycles, out life);
        }
        else
        {
            // 例：Kind 是 NvmeSsd 卻只有 ATA SMART，或只有 NvmeHealth 但 Kind 不是 NvmeSsd，兩邊都不能判讀。
            insufficient = true;
            findings.Add(Find(FindingSeverity.Info, "HEALTH_FORMAT_MISMATCH", S.T("H006"),
                S.F("H007", disk.Kind),
                S.T("H008"),
                S.T("H009")));
        }

        if (scan is not null)
            AnalyzeScan(scan, findings);

        HealthStatus status = insufficient
            ? scan is not null && scan.ErrorBlocks > 0 ? HealthStatus.Danger : HealthStatus.InsufficientData
            : findings.Any(f => f.Severity == FindingSeverity.Danger) ? HealthStatus.Danger
            : findings.Any(f => f.Severity == FindingSeverity.Caution) ? HealthStatus.Caution
            : HealthStatus.Normal;

        return new Diagnosis
        {
            Status = status,
            StatusText = StatusTextOf(status),
            Summary = SummaryOf(status),
            Findings = findings.OrderByDescending(f => f.Severity).ToList(),
            TemperatureCelsius = temp,
            PowerOnHours = powerOnHours,
            PowerCycles = powerCycles,
            LifeRemainingPercent = life,
        };
    }

    private static void AnalyzeAta(
        PhysicalDiskInfo disk, AtaSmartData ata, List<Finding> findings,
        out int? temp, out ulong? powerOnHours, out ulong? powerCycles, out int? lifeRemainingPercent)
    {
        temp = null;
        powerOnHours = null;
        powerCycles = null;
        lifeRemainingPercent = null;

        foreach (var a in ata.Attributes)
        {
            if (a.Threshold is > 0 && a.Current <= a.Threshold.Value)
                findings.Add(Find(FindingSeverity.Danger, "ATA_THRESHOLD", S.F("H010", a.Id),
                    S.F("H011", a.Id, a.Name, a.Current, a.Threshold.Value),
                    S.T("H012"),
                    S.T("H013")));
        }

        if (ata.SmartReturnStatusFailing == true)
            findings.Add(Find(FindingSeverity.Danger, "ATA_FAILING_NOW", S.T("H014"),
                S.T("H015"),
                S.T("H016"),
                S.T("H017")));

        AddRawCount(ata, 0x05, S.T("H018"), findings,
            S.T("H019"),
            S.T("H020"),
            S.T("H021"));

        AddRawCount(ata, 0xC5, S.T("H022"), findings,
            S.T("H023"),
            S.T("H024"),
            S.T("H025"));

        AddRawCount(ata, 0xC6, S.T("H026"), findings,
            S.T("H027"),
            S.T("H028"),
            S.T("H029"));

        var c7 = ata.Get(0xC7);
        if (c7 is not null && c7.Raw > 0)
            findings.Add(Find(FindingSeverity.Info, "ATA_C7", S.F("H030", c7.Raw),
                S.F("H031", c7.Raw),
                S.T("H032"),
                S.T("H033")));

        temp = ReadTemperature(ata);
        int tempCaution = disk.Kind == DriveKind.SataSsd ? HealthRules.SsdTempCaution : HealthRules.HddTempCaution;
        if (temp >= tempCaution)
            findings.Add(Find(FindingSeverity.Caution, "TEMP_HIGH", S.F("H034", temp),
                S.F("H035", temp, tempCaution),
                S.T("H036"),
                S.T("H037")));

        var a09 = ata.Get(0x09);
        if (a09 is not null) powerOnHours = a09.Raw & 0xFFFF_FFFF;

        var a0C = ata.Get(0x0C);
        if (a0C is not null) powerCycles = a0C.Raw & 0xFFFF_FFFF;

        if (disk.Kind == DriveKind.SataSsd)
        {
            lifeRemainingPercent = SsdLifeRules.Read(disk, ata);
            if (lifeRemainingPercent <= HealthRules.SsdLifeCaution)
            {
                findings.Add(Find(FindingSeverity.Caution, "SSD_LIFE_LOW", S.F("H038", lifeRemainingPercent),
                    S.F("H039", lifeRemainingPercent),
                    S.T("H040"),
                    S.T("H041")));
            }
        }
    }

    private static void AnalyzeNvme(
        NvmeHealthLog log, List<Finding> findings,
        out int? temp, out ulong? powerOnHours, out ulong? powerCycles, out int? lifeRemainingPercent)
    {
        temp = log.TemperatureCelsius;
        powerOnHours = log.PowerOnHours;
        powerCycles = log.PowerCycles;
        lifeRemainingPercent = Math.Max(0, 100 - (int)log.PercentageUsed);

        if (log.CriticalWarning != 0)
        {
            string[] bits =
            {
                S.T("H042"),
                S.T("H043"),
                S.T("H044"),
                S.T("H045"),
                S.T("H046"),
            };
            for (int bit = 0; bit < bits.Length; bit++)
            {
                if ((log.CriticalWarning & (1 << bit)) == 0) continue;
                findings.Add(Find(FindingSeverity.Danger, "NVME_WARNING", S.F("H047", bits[bit]),
                    S.F("H048", log.CriticalWarning, bit, bits[bit]),
                    bit switch
                    {
                        0 => S.T("H049"),
                        1 => S.T("H050"),
                        2 => S.T("H051"),
                        3 => S.T("H052"),
                        _ => S.T("H053"),
                    },
                    S.T("H054")));
            }

            if ((log.CriticalWarning & ~0x1F) != 0)
                findings.Add(Find(FindingSeverity.Danger, "NVME_WARNING_UNKNOWN", S.F("H055", log.CriticalWarning),
                    S.T("H056"),
                    S.T("H057"),
                    S.T("H058")));
        }

        if (log.AvailableSpare < log.AvailableSpareThreshold)
            findings.Add(Find(FindingSeverity.Danger, "NVME_SPARE_LOW", S.F("H059", log.AvailableSpare, log.AvailableSpareThreshold),
                S.F("H060", log.AvailableSpare, log.AvailableSpareThreshold),
                S.T("H061"),
                S.T("H062")));

        if (log.MediaErrors > 0)
            findings.Add(Find(FindingSeverity.Danger, "NVME_MEDIA_ERRORS", S.F("H063", log.MediaErrors),
                S.F("H064", log.MediaErrors),
                S.T("H065"),
                S.T("H066")));

        if (log.PercentageUsed >= HealthRules.NvmeUsedCaution)
            findings.Add(Find(FindingSeverity.Caution, "NVME_USED_HIGH", S.F("H067", log.PercentageUsed),
                S.F("H068", log.PercentageUsed, HealthRules.NvmeUsedCaution),
                S.T("H069"),
                S.T("H070")));

        if (temp >= HealthRules.NvmeTempCaution)
            findings.Add(Find(FindingSeverity.Caution, "TEMP_HIGH", S.F("H071", temp),
                S.F("H072", temp, HealthRules.NvmeTempCaution),
                S.T("H073"),
                S.T("H074")));
    }

    private static void AnalyzeScan(ScanResult scan, List<Finding> findings)
    {
        if (scan.ErrorBlocks > 0)
            findings.Add(Find(FindingSeverity.Danger, "SCAN_ERROR", S.F("H075", scan.ErrorBlocks),
                S.F("H076", scan.ErrorBlocks),
                S.T("H077"),
                S.T("H078")));

        long tested = (long)scan.OkBlocks + scan.SlowBlocks + scan.ErrorBlocks;
        double slowRatio = tested > 0 ? (double)scan.SlowBlocks / tested : 0;
        if (tested > 0 && slowRatio > HealthRules.SlowBlockCautionRatio)
            findings.Add(Find(FindingSeverity.Caution, "SCAN_SLOW", S.F("H079", slowRatio * 100),
                S.F("H080", tested, scan.SlowBlocks, slowRatio * 100),
                S.T("H081"),
                S.T("H082")));

        if (scan.Cancelled)
            findings.Add(Find(FindingSeverity.Info, "SCAN_INCOMPLETE", S.T("H083"),
                scan.AbortReason is null
                    ? S.T("H084")
                    : S.F("H085", scan.AbortReason),
                S.T("H086"),
                S.T("H087")));
    }

    private static void AddRawCount(
        AtaSmartData ata, byte id, string name, List<Finding> findings,
        string detailFormat, string impact, string advice)
    {
        var a = ata.Get(id);
        if (a is null || a.Raw == 0) return;
        findings.Add(Find(FindingSeverity.Caution, $"ATA_{id:X2}", S.F("H088", name, id, a.Raw),
            string.Format(detailFormat, a.Raw), impact, advice));
    }

    // C2（Temperature）優先，低階位元組不在可採信範圍時才改看 BE（Airflow Temperature）。
    private static int? ReadTemperature(AtaSmartData ata)
    {
        foreach (byte id in new byte[] { 0xC2, 0xBE })
        {
            var attr = ata.Get(id);
            if (attr is null) continue;
            int value = (int)(attr.Raw & 0xFF);
            if (value is >= HealthRules.TempMin and <= HealthRules.TempMax) return value;
        }
        return null;
    }

    private static Finding Find(FindingSeverity severity, string code, string title, string detail, string impact, string advice) =>
        new() { Severity = severity, Code = code, Title = title, Detail = detail, Impact = impact, Advice = advice };

    private static string StatusTextOf(HealthStatus status) => status switch
    {
        HealthStatus.Normal => S.T("H089"),
        HealthStatus.Caution => S.T("H090"),
        HealthStatus.Danger => S.T("H091"),
        _ => S.T("H092"),
    };

    private static string SummaryOf(HealthStatus status) => status switch
    {
        HealthStatus.Normal => S.T("H093"),
        HealthStatus.Caution => S.T("H094"),
        HealthStatus.Danger => S.T("H095"),
        _ => S.T("H096"),
    };
}
