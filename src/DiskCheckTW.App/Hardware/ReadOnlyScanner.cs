using DiskCheckTW.App;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskCheckTW.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskCheckTW.App.Hardware;

public sealed record ScanProgress(
    int CompletedBlocks, int BlocksTotal, int BlockIndex, BlockState BlockState,
    long BytesScanned, TimeSpan Elapsed, int ErrorCount, int SlowCount,
    double AvgLatencyMs, double MaxLatencyMs);

public sealed class ReadOnlyScanner
{
    public const int ReadBytes = 1024 * 1024;
    public const int TimeoutSeconds = 10;
    public const int MaxRetries = 2;
    public const int SlowReadMs = 300;
    private const int MaxBlocks = 2000;
    private const int MaxErrorOffsets = 1000;
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint NoBufferingOverlapped = 0x20000000 | 0x40000000;
    private const int IoPending = 997;
    private const int TimeoutError = 1460;

    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr Event;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadFile(SafeFileHandle file, void* buffer, uint count,
        out uint read, Overlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool GetOverlappedResult(SafeFileHandle file, Overlapped* overlapped,
        out uint read, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool CancelIoEx(SafeFileHandle file, Overlapped* overlapped);

    internal static (int Alignment, int SegmentBytes, long ReadableBytes, long Segments, int Blocks) Layout(long sizeBytes, int? sectorSize)
    {
        var sector = sectorSize is > 0 ? sectorSize.Value : 512;
        var gcd = Gcd(sector, 4096);
        var alignment = checked(sector / gcd * 4096);
        var segmentBytes = ReadBytes / alignment * alignment;
        if (segmentBytes == 0 || sizeBytes <= 0) return (alignment, segmentBytes, 0, 0, 0);
        var readable = sizeBytes / alignment * alignment;
        var segments = readable / segmentBytes + (readable % segmentBytes == 0 ? 0 : 1);
        return (alignment, segmentBytes, readable, segments, (int)Math.Min(MaxBlocks, segments));
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    public Task<ScanResult> RunAsync(PhysicalDiskInfo disk, ScanMode mode, IProgress<ScanProgress> progress, CancellationToken ct) =>
        Task.Run(() => Run(disk, mode, progress, ct));

    private static unsafe ScanResult Run(PhysicalDiskInfo disk, ScanMode mode, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var started = DateTime.Now;
        var layout = Layout(disk.SizeBytes, disk.LogicalSectorSize);
        var result = new ScanResult { Mode = mode, StartedAt = started, BlocksTotal = layout.Blocks };
        if (layout.Blocks == 0)
        {
            result.AbortReason = L.T("U133");
            result.Elapsed = watch.Elapsed;
            return result;
        }
        if (ct.IsCancellationRequested) { result.Cancelled = true; result.Elapsed = watch.Elapsed; return result; }

        using var file = CreateFileW(disk.DevicePath, GenericRead, ShareReadWrite,
            IntPtr.Zero, OpenExisting, NoBufferingOverlapped, IntPtr.Zero);
        if (file.IsInvalid)
        {
            var code = Marshal.GetLastWin32Error();
            result.AbortReason = code == 5 ? L.T("U134")
                : L.F("U135", code);
            result.Elapsed = watch.Elapsed;
            return result;
        }

        var buffer = NativeMemory.AlignedAlloc((nuint)ReadBytes, 4096);
        if (buffer == null) { result.AbortReason = L.T("U136"); result.Elapsed = watch.Elapsed; return result; }
        try
        {
            double latencySum = 0;
            int successCount = 0;
            int errorCount = 0;
            long lastReportMs = -100;
            void Report(int blockIndex, BlockState state, bool completed)
            {
                var now = watch.ElapsedMilliseconds;
                if (!completed && now - lastReportMs < 100) return;
                lastReportMs = now;
                progress.Report(new ScanProgress(result.OkBlocks + result.SlowBlocks + result.ErrorBlocks,
                    result.BlocksTotal, blockIndex, state, result.BytesScanned, watch.Elapsed,
                    errorCount, result.SlowBlocks, result.AvgLatencyMs, result.MaxLatencyMs));
            }

            for (var block = 0; block < layout.Blocks; block++)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
                var first = layout.Segments / layout.Blocks * block + layout.Segments % layout.Blocks * block / layout.Blocks;
                var next = layout.Segments / layout.Blocks * (block + 1) + layout.Segments % layout.Blocks * (block + 1) / layout.Blocks;
                var sample = first + (next - first - 1) / 2;
                var blockState = BlockState.Ok;
                for (var segment = mode == ScanMode.Full ? first : sample;
                     segment < (mode == ScanMode.Full ? next : sample + 1); segment++)
                {
                    if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
                    var offset = segment * layout.SegmentBytes;
                    var length = (uint)Math.Min(layout.SegmentBytes, layout.ReadableBytes - offset);
                    var state = BlockState.Error;
                    var error = 0;
                    for (var attempt = 0; attempt <= MaxRetries; attempt++)
                    {
                        var (ok, elapsedMs, win32) = Read(file, buffer, offset, length, ct);
                        error = win32;
                        if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
                        if (ok)
                        {
                            state = elapsedMs >= SlowReadMs ? BlockState.Slow : BlockState.Ok;
                            result.BytesScanned += length;
                            successCount++;
                            latencySum += elapsedMs;
                            result.AvgLatencyMs = latencySum / successCount;
                            result.MaxLatencyMs = Math.Max(result.MaxLatencyMs, elapsedMs);
                            break;
                        }
                        if (IsFatal(error)) break;
                    }
                    if (result.Cancelled) break;
                    if (state == BlockState.Error)
                    {
                        errorCount++;
                        if (result.ErrorOffsets.Count < MaxErrorOffsets) result.ErrorOffsets.Add(offset);
                    }
                    if (state > blockState) blockState = state;
                    Report(block, BlockState.Untested, false);
                    if (IsFatal(error) && state == BlockState.Error)
                    {
                        result.AbortReason = L.F("U137", error);
                        break;
                    }
                }
                if (result.Cancelled || result.AbortReason is not null) break;
                if (blockState == BlockState.Ok) result.OkBlocks++;
                else if (blockState == BlockState.Slow) result.SlowBlocks++;
                else result.ErrorBlocks++;
                Report(block, blockState, true);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
            result.Elapsed = watch.Elapsed;
        }
        return result;
    }

    private static bool IsFatal(int error) => error is 1167 or 433 or 21 or 55;

    private static unsafe (bool Ok, double Ms, int Error) Read(SafeFileHandle file, void* buffer,
        long offset, uint length, CancellationToken ct)
    {
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset);
        var overlapped = new Overlapped
        {
            Offset = (uint)offset,
            OffsetHigh = (uint)((ulong)offset >> 32),
            Event = signal.SafeWaitHandle.DangerousGetHandle()
        };
        var pointer = (IntPtr)(&overlapped);
        using var registration = ct.Register(() => { unsafe { CancelIoEx(file, (Overlapped*)pointer); } });
        var timer = Stopwatch.StartNew();
        var ok = ReadFile(file, buffer, length, out var bytes, &overlapped);
        var error = ok ? 0 : Marshal.GetLastWin32Error();
        if (!ok && error == IoPending)
        {
            var signaled = signal.WaitOne(TimeSpan.FromSeconds(TimeoutSeconds));
            if (!signaled || ct.IsCancellationRequested) CancelIoEx(file, &overlapped);
            ok = GetOverlappedResult(file, &overlapped, out bytes, true);
            error = !signaled ? TimeoutError : ok ? 0 : Marshal.GetLastWin32Error();
            if (!signaled) ok = false;
        }
        timer.Stop();
        return (ok && bytes == length, timer.Elapsed.TotalMilliseconds, ok && bytes != length ? 38 : error);
    }
}
