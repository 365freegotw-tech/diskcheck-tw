using DiskCheckTW.App;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DiskCheckTW.Core.Models;

namespace DiskCheckTW.App.Hardware;

public static class DiskDiscovery
{
    public static IReadOnlyList<PhysicalDiskInfo> Discover()
    {
        var letters = GetLetters();
        var systemLetter = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))?.TrimEnd('\\');
        var disks = new List<PhysicalDiskInfo>();
        for (var index = 0; index < 32; index++)
        {
            using var readHandle = NativeDisk.Open($@"\\.\PhysicalDrive{index}", read: true);
            using var noAccessHandle = readHandle.IsInvalid ? NativeDisk.Open($@"\\.\PhysicalDrive{index}") : null;
            var handle = readHandle.IsInvalid ? noAccessHandle! : readHandle;
            if (handle.IsInvalid) continue;
            var descriptor = NativeDisk.Query(handle, 0);
            var length = new byte[8];
            var hasLength = NativeDisk.Ioctl(handle, NativeDisk.DiskLength, null, length, out var returned) && returned >= 8;
            if (descriptor is null && !hasLength) continue;

            var bus = descriptor is { Length: >= 32 } ? BusName(NativeDisk.U32(descriptor, 28)) : "Unknown";
            var seek = NativeDisk.Query(handle, 7);
            var kind = bus == "NVMe" ? DriveKind.NvmeSsd
                : seek is { Length: >= 9 } ? (seek[8] != 0 ? DriveKind.Hdd : DriveKind.SataSsd)
                : DriveKind.Unknown;
            if (kind == DriveKind.SataSsd && bus is "USB" or "RAID") kind = DriveKind.Unknown;
            var alignment = NativeDisk.Query(handle, 6);
            var diskLetters = letters.Where(x => x.Value.Contains(index)).Select(x => x.Key).ToArray();
            var model = DescriptorString(descriptor, 16) ?? "";
            disks.Add(new PhysicalDiskInfo
            {
                Index = index,
                Vendor = DescriptorString(descriptor, 12) ?? InferVendor(model),
                Model = model,
                Firmware = DescriptorString(descriptor, 20),
                Serial = DescriptorString(descriptor, 24),
                SizeBytes = hasLength ? (long)NativeDisk.U64(length, 0) : 0,
                LogicalSectorSize = alignment is { Length: >= 20 } && NativeDisk.U32(alignment, 16) is > 0 and <= int.MaxValue
                    ? (int?)NativeDisk.U32(alignment, 16) : null,
                Kind = kind,
                BusType = bus,
                DriveLetters = diskLetters,
                IsSystemDisk = systemLetter is not null && diskLetters.Contains(systemLetter, StringComparer.OrdinalIgnoreCase)
            });
        }
        return disks;
    }

    private static Dictionary<string, HashSet<int>> GetLetters()
    {
        var result = new Dictionary<string, HashSet<int>>();
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            using var handle = NativeDisk.Open($@"\\.\{letter}:");
            if (handle.IsInvalid) continue;
            var buffer = new byte[4096];
            var ok = NativeDisk.Ioctl(handle, NativeDisk.VolumeExtents, null, buffer, out var returned);
            while (!ok && (Marshal.GetLastWin32Error() is 122 or 234) && buffer.Length < 1024 * 1024)
            {
                buffer = new byte[buffer.Length * 2];
                ok = NativeDisk.Ioctl(handle, NativeDisk.VolumeExtents, null, buffer, out returned);
            }
            if (!ok || returned < 32) continue;
            var count = NativeDisk.U32(buffer, 0);
            if (count > (returned - 8) / 24) continue;
            var numbers = new HashSet<int>();
            for (var i = 0; i < count; i++) numbers.Add((int)NativeDisk.U32(buffer, 8 + (int)i * 24));
            result[$"{letter}:"] = numbers;
        }
        return result;
    }

    private static string? DescriptorString(byte[]? descriptor, int offset)
    {
        if (descriptor is null || descriptor.Length < offset + 4) return null;
        var position = NativeDisk.U32(descriptor, offset);
        if (position == 0 || position >= descriptor.Length) return null;
        var end = Array.IndexOf(descriptor, (byte)0, (int)position);
        if (end < 0) end = descriptor.Length;
        var value = Encoding.ASCII.GetString(descriptor, (int)position, end - (int)position).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? InferVendor(string model)
    {
        var name = model.TrimStart();
        if (name.StartsWith("Samsung", StringComparison.OrdinalIgnoreCase)) return "Samsung";
        if (name.StartsWith("WDC", StringComparison.OrdinalIgnoreCase) || name.StartsWith("WD", StringComparison.OrdinalIgnoreCase)) return "Western Digital";
        if (name.StartsWith("Seagate", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ST", StringComparison.OrdinalIgnoreCase)) return "Seagate";
        if (name.StartsWith("TOSHIBA", StringComparison.OrdinalIgnoreCase)) return "TOSHIBA";
        if (name.StartsWith("Crucial", StringComparison.OrdinalIgnoreCase) || name.StartsWith("CT", StringComparison.OrdinalIgnoreCase)) return "Crucial";
        if (name.StartsWith("Kingston", StringComparison.OrdinalIgnoreCase)) return "Kingston";
        if (name.StartsWith("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        if (name.StartsWith("SanDisk", StringComparison.OrdinalIgnoreCase)) return "SanDisk";
        if (name.StartsWith("HGST", StringComparison.OrdinalIgnoreCase)) return "HGST";
        if (name.StartsWith("Micron", StringComparison.OrdinalIgnoreCase)) return "Micron";
        if (name.StartsWith("SK hynix", StringComparison.OrdinalIgnoreCase)) return "SK hynix";
        if (name.StartsWith("ADATA", StringComparison.OrdinalIgnoreCase)) return "ADATA";
        if (name.StartsWith("Transcend", StringComparison.OrdinalIgnoreCase)) return "Transcend";
        if (name.StartsWith("Lexar", StringComparison.OrdinalIgnoreCase)) return "Lexar";
        if (name.StartsWith("Phison", StringComparison.OrdinalIgnoreCase)) return "Phison";
        return null;
    }

    private static string BusName(uint bus) => bus switch
    {
        3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA", 12 => "SD", 14 => "Virtual", 17 => "NVMe", 18 => "SCM", 19 => "UFS", _ => "Unknown"
    };
}
