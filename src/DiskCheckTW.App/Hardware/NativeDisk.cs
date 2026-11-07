using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskCheckTW.App.Hardware;

internal static class NativeDisk
{
    internal const uint StorageQuery = 0x002D1400;
    internal const uint DiskLength = 0x0007405C;
    internal const uint VolumeExtents = 0x00560000;
    internal const uint AtaPassThrough = 0x0004D02C;
    internal const uint SmartReceive = 0x0007C088;
    internal const uint ShareReadWrite = 1 | 2;
    internal const uint OpenExisting = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code,
        byte[]? input, int inputLength, [Out] byte[] output, int outputLength,
        out int returned, IntPtr overlapped);

    internal static SafeFileHandle Open(string path, bool read = false) =>
        CreateFileW(path, read ? 0x80000000 : 0, ShareReadWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    // SMART/NVMe DeviceIoControl queries require both access rights; this handle is never used for file writes.
    internal static SafeFileHandle OpenForSmartQuery(string path) =>
        CreateFileW(path, 0x80000000 | 0x40000000 /* GENERIC_WRITE */, ShareReadWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    internal static bool Ioctl(SafeFileHandle handle, uint code, byte[]? input, byte[] output, out int returned) =>
        DeviceIoControl(handle, code, input, input?.Length ?? 0, output, output.Length,
            out returned, IntPtr.Zero);

    internal static byte[]? Query(SafeFileHandle handle, uint property, int size = 1024)
    {
        var input = new byte[12]; // STORAGE_PROPERTY_QUERY includes AdditionalParameters[1]
        BinaryPrimitives.WriteUInt32LittleEndian(input, property);
        var output = new byte[size];
        return Ioctl(handle, StorageQuery, input, output, out var returned) && returned >= 8
            ? output.AsSpan(0, returned).ToArray() : null;
    }

    internal static uint U32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    internal static ulong U64(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
}
