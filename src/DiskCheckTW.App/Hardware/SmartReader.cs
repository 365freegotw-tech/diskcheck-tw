using DiskCheckTW.App;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using DiskCheckTW.Core.Analysis;
using DiskCheckTW.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskCheckTW.App.Hardware;

public static class SmartReader
{
    private const int Sector = 512;
    public static PhysicalDiskInfo Read(PhysicalDiskInfo disk)
    {
        AtaSmartData? ata = null;
        NvmeHealthLog? nvme = null;
        string? reason = null;
        try
        {
            if (disk.BusType is "ATA" or "SATA" or "NVMe")
            {
                using var smartHandle = NativeDisk.OpenForSmartQuery(disk.DevicePath);
                var openError = smartHandle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
                using var readHandle = smartHandle.IsInvalid && disk.BusType == "NVMe"
                    ? NativeDisk.Open(disk.DevicePath, read: true) : null;
                var handle = smartHandle.IsInvalid ? readHandle : smartHandle;
                if (handle is null || handle.IsInvalid)
                    reason = (readHandle is null ? openError : Marshal.GetLastWin32Error()) == 5
                        ? L.T("U123") : L.T("U124");
                else if (disk.BusType == "NVMe")
                {
                    nvme = ReadNvme(handle);
                    if (nvme is null) reason = L.T("U125");
                }
                else
                {
                    ata = ReadAta(handle, disk);
                    if (ata is null) reason = Marshal.GetLastWin32Error() == 5
                        ? L.T("U126")
                        : L.T("U127");
                }
            }
            else reason = disk.BusType switch
            {
                "USB" => L.T("U128"),
                "RAID" => L.T("U129"),
                _ => L.T("U130")
            };
        }
        catch (Exception ex)
        {
            reason = ex is UnauthorizedAccessException ? L.T("U131") : L.F("U132", ex.Message);
        }
        return new PhysicalDiskInfo
        {
            Index = disk.Index, Model = disk.Model, Vendor = disk.Vendor, Serial = disk.Serial,
            Firmware = disk.Firmware, SizeBytes = disk.SizeBytes, LogicalSectorSize = disk.LogicalSectorSize,
            Kind = disk.Kind, BusType = disk.BusType, DriveLetters = disk.DriveLetters,
            IsSystemDisk = disk.IsSystemDisk, AtaSmart = ata, NvmeHealth = nvme,
            HealthUnavailableReason = reason
        };
    }

    private static AtaSmartData? ReadAta(SafeFileHandle handle, PhysicalDiskInfo disk)
    {
        var driveIndex = disk.Index;
        var data = ReadAtaPage(handle, 0xD0, driveIndex);
        if (data is null) return null;
        var thresholds = ReadAtaPage(handle, 0xD1, driveIndex);
        bool? failing = null;
        var status = AtaCommand(handle, 0xDA, 0);
        if (status is { Length: >= 48 })
        {
            var low = status[43];
            var high = status[44];
            if (low == 0x4F && high == 0xC2) failing = false;
            else if (low == 0xF4 && high == 0x2C) failing = true;
        }
        else failing = ReadLegacyStatus(handle, driveIndex);
        var limits = new Dictionary<byte, byte>();
        if (thresholds is not null)
            for (var i = 0; i < 30; i++)
            {
                var p = 2 + i * 12;
                if (thresholds[p] != 0) limits[thresholds[p]] = thresholds[p + 1];
            }
        var attributes = new List<SmartAttribute>();
        for (var i = 0; i < 30; i++)
        {
            var p = 2 + i * 12;
            var id = data[p];
            if (id == 0) continue;
            ulong raw = 0;
            for (var j = 0; j < 6; j++) raw |= (ulong)data[p + 5 + j] << (8 * j);
            attributes.Add(new SmartAttribute
            {
                Id = id,
                Flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 1, 2)),
                Current = data[p + 3], Worst = data[p + 4],
                Threshold = limits.TryGetValue(id, out var limit) ? limit : null, Raw = raw
            });
        }
        if (attributes.Count == 0) return null;
        var profile = SmartProfiles.Detect(disk, attributes);
        var named = attributes.Select(a => new SmartAttribute
        {
            Id = a.Id, Name = SmartProfiles.NameOf(profile, a.Id), Flags = a.Flags,
            Current = a.Current, Worst = a.Worst, Threshold = a.Threshold, Raw = a.Raw
        }).ToArray();
        return new AtaSmartData { Attributes = named, VendorProfile = profile, SmartReturnStatusFailing = failing };
    }

    private static bool? ReadLegacyStatus(SafeFileHandle handle, int driveIndex)
    {
        var input = new byte[33]; // SENDCMDINPARAMS, pack 1
        input[4] = 0xDA;
        input[5] = 1;
        input[6] = 1;
        input[7] = 0x4F;
        input[8] = 0xC2;
        input[9] = (byte)(0xA0 | ((driveIndex & 1) << 4));
        input[10] = 0xB0;
        input[12] = (byte)driveIndex;
        var output = new byte[16 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(input, 8); // sizeof(IDEREGS)
        if (!NativeDisk.Ioctl(handle, NativeDisk.SmartReceive, input, output, out var returned) || returned < output.Length || output[4] != 0)
            return null;
        if (output[19] == 0x4F && output[20] == 0xC2) return false;
        if (output[19] == 0xF4 && output[20] == 0x2C) return true;
        return null;
    }

    private static byte[]? ReadAtaPage(SafeFileHandle handle, byte feature, int driveIndex)
    {
        var pass = AtaCommand(handle, feature, Sector);
        if (pass is { Length: >= 48 + Sector }) return pass.AsSpan(48, Sector).ToArray();
        var input = new byte[33]; // SENDCMDINPARAMS, pack 1
        BinaryPrimitives.WriteUInt32LittleEndian(input, Sector);
        input[4] = feature;
        input[5] = 1;
        input[6] = 1;
        input[7] = 0x4F;
        input[8] = 0xC2;
        input[9] = (byte)(0xA0 | ((driveIndex & 1) << 4));
        input[10] = 0xB0;
        input[12] = (byte)driveIndex;
        var output = new byte[16 + Sector]; // SENDCMDOUTPARAMS header + one sector
        return NativeDisk.Ioctl(handle, NativeDisk.SmartReceive, input, output, out var returned)
            && returned >= 16 + Sector && output[4] == 0
            ? output.AsSpan(16, Sector).ToArray() : null;
    }

    private static byte[]? AtaCommand(SafeFileHandle handle, byte feature, int size)
    {
        if (feature is not (0xD0 or 0xD1 or 0xDA) || (feature == 0xDA ? size != 0 : size != Sector))
            throw new ArgumentOutOfRangeException(nameof(feature), "Only SMART READ DATA, READ THRESHOLDS and RETURN STATUS are allowed.");
        var buffer = new byte[48 + size]; // ATA_PASS_THROUGH_EX is 48 bytes on x64
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), 48);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), (ushort)(1 | (size > 0 ? 2 : 0))); // DRDY_REQUIRED | DATA_IN
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), 5);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(24, 8), 48);
        buffer[40] = feature;
        buffer[41] = (byte)(size > 0 ? 1 : 0);
        buffer[42] = 1;
        buffer[43] = 0x4F;
        buffer[44] = 0xC2;
        buffer[45] = 0xA0;
        buffer[46] = 0xB0;
        var output = new byte[buffer.Length];
        return NativeDisk.Ioctl(handle, NativeDisk.AtaPassThrough, buffer, output, out var returned)
            && returned >= output.Length && (output[46] & 0x21) == 0 ? output : null;
    }

    private static NvmeHealthLog? ReadNvme(SafeFileHandle handle)
    {
        foreach (uint property in new uint[] { 50, 49 }) // device then adapter protocol-specific property
        {
            var buffer = new byte[8 + 40 + Sector];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), property);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), 3); // ProtocolTypeNvme
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), 2); // NVMeDataTypeLogPage
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16, 4), 2); // health log 02h
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24, 4), 40);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28, 4), Sector);
            var output = new byte[buffer.Length];
            if (!NativeDisk.Ioctl(handle, NativeDisk.StorageQuery, buffer, output, out var returned) || returned < 48 + Sector) continue;
            if (NativeDisk.U32(output, 0) != 48 || NativeDisk.U32(output, 8) != 3 || NativeDisk.U32(output, 12) != 2) continue;
            var offset = NativeDisk.U32(output, 24);
            var length = NativeDisk.U32(output, 28);
            if (offset < 40 || length < Sector || 8UL + offset + Sector > (ulong)returned) continue;
            var p = 8 + (int)offset;
            return new NvmeHealthLog
            {
                CriticalWarning = output[p], TemperatureKelvin = BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(p + 1, 2)),
                AvailableSpare = output[p + 3], AvailableSpareThreshold = output[p + 4], PercentageUsed = output[p + 5],
                DataUnitsRead = NativeDisk.U64(output, p + 32), DataUnitsWritten = NativeDisk.U64(output, p + 48),
                PowerCycles = NativeDisk.U64(output, p + 112), PowerOnHours = NativeDisk.U64(output, p + 128),
                UnsafeShutdowns = NativeDisk.U64(output, p + 144), MediaErrors = NativeDisk.U64(output, p + 160),
                ErrorLogEntries = NativeDisk.U64(output, p + 176)
            };
        }
        return null;
    }
}
