namespace DiskCheckTW.Core.Models;

public enum DriveKind { Unknown, Hdd, SataSsd, NvmeSsd }

/// <summary>一顆實體磁碟（不是分割區）。硬體層填入，診斷與報告只讀。</summary>
public sealed class PhysicalDiskInfo
{
    public int Index { get; init; }                      // \\.\PhysicalDriveN 的 N
    public string DevicePath => $@"\\.\PhysicalDrive{Index}";
    public string Model { get; init; } = "";
    public string? Vendor { get; init; }
    public string? Serial { get; init; }
    public string? Firmware { get; init; }
    public long SizeBytes { get; init; }
    public int? LogicalSectorSize { get; init; }
    public DriveKind Kind { get; init; }
    public string BusType { get; init; } = "Unknown";     // SATA、NVMe、USB、RAID…；"Unknown" 為內部代碼，顯示時再翻譯
    public IReadOnlyList<string> DriveLetters { get; init; } = Array.Empty<string>();
    public bool IsSystemDisk { get; init; }
    /// <summary>無法取得健康資料的原因（USB 外接盒、RAID、權限…）；null 表示有取得。</summary>
    public string? HealthUnavailableReason { get; init; }
    public AtaSmartData? AtaSmart { get; init; }
    public NvmeHealthLog? NvmeHealth { get; init; }
}

public sealed class SmartAttribute
{
    public byte Id { get; init; }
    public string Name { get; init; } = "";
    public byte Current { get; init; }
    public byte Worst { get; init; }
    public byte? Threshold { get; init; }   // null = 取不到門檻
    public ulong Raw { get; init; }          // 48-bit 原始值
    public ushort Flags { get; init; }
    public bool IsPrefail => (Flags & 0x1) != 0;
}

public sealed class AtaSmartData
{
    public IReadOnlyList<SmartAttribute> Attributes { get; init; } = Array.Empty<SmartAttribute>();
    public string? VendorProfile { get; init; }          // CDI Smart* 名稱表設定檔
    public bool? SmartReturnStatusFailing { get; init; }   // SMART RETURN STATUS，null = 無法取得
    public SmartAttribute? Get(byte id) => Attributes.FirstOrDefault(a => a.Id == id);
}

/// <summary>NVMe SMART / Health Information Log (Log ID 02h)。</summary>
public sealed class NvmeHealthLog
{
    public byte CriticalWarning { get; init; }
    public int TemperatureKelvin { get; init; }
    public byte AvailableSpare { get; init; }            // %
    public byte AvailableSpareThreshold { get; init; }   // %
    public byte PercentageUsed { get; init; }            // %，可 >100
    public ulong DataUnitsRead { get; init; }            // 單位 512,000 bytes
    public ulong DataUnitsWritten { get; init; }
    public ulong PowerCycles { get; init; }
    public ulong PowerOnHours { get; init; }
    public ulong UnsafeShutdowns { get; init; }
    public ulong MediaErrors { get; init; }
    public ulong ErrorLogEntries { get; init; }
    public int? TemperatureCelsius => TemperatureKelvin > 0 ? TemperatureKelvin - 273 : null;
}

public enum HealthStatus { Normal, Caution, Danger, InsufficientData }

public enum FindingSeverity { Info, Caution, Danger }

public sealed class Finding
{
    public FindingSeverity Severity { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";    // 例：「重新分配磁區 (05) 有 8 個」
    public string Detail { get; init; } = "";   // 發現什麼
    public string Impact { get; init; } = "";   // 可能影響
    public string Advice { get; init; } = "";   // 建議行動
}

public sealed class Diagnosis
{
    public HealthStatus Status { get; init; }
    public string StatusText { get; init; } = "";   // 正常／注意／危險／資料不足
    public string Summary { get; init; } = "";
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
    public int? TemperatureCelsius { get; init; }
    public ulong? PowerOnHours { get; init; }
    public ulong? PowerCycles { get; init; }
    public int? LifeRemainingPercent { get; init; }  // 僅 SSD 且資料可信時
}

public enum BlockState : byte { Untested = 0, Ok = 1, Slow = 2, Error = 3 }

public enum ScanMode { QuickSample, Full }

public sealed class ScanResult
{
    public ScanMode Mode { get; init; }
    public DateTime StartedAt { get; init; }
    public TimeSpan Elapsed { get; set; }
    public long BytesScanned { get; set; }
    public int BlocksTotal { get; init; }
    public int OkBlocks { get; set; }
    public int SlowBlocks { get; set; }
    public int ErrorBlocks { get; set; }
    public double AvgLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public bool Cancelled { get; set; }
    public string? AbortReason { get; set; }   // 裝置移除、權限不足…
    public List<long> ErrorOffsets { get; } = new();
}
