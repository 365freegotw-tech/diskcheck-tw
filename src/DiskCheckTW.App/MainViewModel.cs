using DiskCheckTW.App;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DiskCheckTW.App.Hardware;
using DiskCheckTW.Core.Analysis;
using DiskCheckTW.Core.Models;

namespace DiskCheckTW.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DiskChoice> Disks { get; } = new();
    public ObservableCollection<Finding> Findings { get; } = new();
    public ObservableCollection<ActionDisplayStep> ActionSteps { get; } = new();
    public ObservableCollection<SmartRow> SmartRows { get; } = new();
    public ObservableCollection<NvmeRow> NvmeRows { get; } = new();
    private DiskChoice? _selectedDisk;
    private bool _busy;
    private string _progress = L.T("U001");
    private string _model = L.T("U002"), _vendor = L.T("U003"), _capacity = L.T("U004"), _kind = L.T("U005"), _bus = L.T("U006");
    private string _serial = L.T("U007"), _letters = L.T("U008"), _temperature = L.T("U009");
    private string _hours = L.T("U010"), _cycles = L.T("U011"), _lifeRemaining = L.T("U012"), _healthText = L.T("U013"), _summary = L.T("U014");
    private Brush _healthBrush = Brushes.Gray;
    private string _actionUrgency = "", _actionHeadline = "", _nextCheckDate = "";
    private Brush _actionBrush = Brushes.Gray, _actionTextBrush = Brushes.White;
    private Visibility _actionVisibility = Visibility.Collapsed, _nextCheckVisibility = Visibility.Collapsed;
    private Visibility _ataVisibility = Visibility.Collapsed, _nvmeVisibility = Visibility.Collapsed;
    private Visibility _smartUnavailableVisibility = Visibility.Visible;
    private Visibility _noFindingsVisibility = Visibility.Collapsed;
    private string _smartUnavailableText = L.T("U015");
    private readonly ReadOnlyScanner _scanner = new();
    private CancellationTokenSource? _scanCancellation;
    private ScanResult? _lastScan;
    private PhysicalDiskInfo? _scannedDisk;
    private string _scanPercent = "0%", _scanCapacity = "0.0 GB", _scanElapsed = L.T("U016");
    private string _scanErrors = "0", _scanSlow = "0", _scanAvg = "0 ms", _scanMax = "0 ms";
    private WriteableBitmap? _scanGrid;
    private const int GridColumns = 50, CellPixels = 9;

    public DiskChoice? SelectedDisk { get => _selectedDisk; set { if (IsScanning || ReferenceEquals(_selectedDisk, value)) return; _selectedDisk = value; LastScan = null; ResetScan(); Changed(); Changed(nameof(CanStartScan)); Show(value?.Disk); } }
    public bool CanRefresh => !_busy && !IsScanning;
    public bool CanSelectDisk => !IsScanning;
    public bool CanStartScan => !_busy && !IsScanning && SelectedDisk is not null;
    public bool IsScanning => _scanCancellation is not null;
    public ScanResult? LastScan { get => _lastScan; private set { _lastScan = value; if (value is null) _scannedDisk = null; Changed(); } }
    public ScanResult? SelectedDiskScan => ReferenceEquals(_scannedDisk, SelectedDisk?.Disk) ? LastScan : null;
    public WriteableBitmap? ScanGrid { get => _scanGrid; private set { _scanGrid = value; Changed(); } }
    public string ScanPercent { get => _scanPercent; private set { _scanPercent = value; Changed(); } }
    public string ScanCapacity { get => _scanCapacity; private set { _scanCapacity = value; Changed(); } }
    public string ScanElapsed { get => _scanElapsed; private set { _scanElapsed = value; Changed(); } }
    public string ScanErrors { get => _scanErrors; private set { _scanErrors = value; Changed(); } }
    public string ScanSlow { get => _scanSlow; private set { _scanSlow = value; Changed(); } }
    public string ScanAvg { get => _scanAvg; private set { _scanAvg = value; Changed(); } }
    public string ScanMax { get => _scanMax; private set { _scanMax = value; Changed(); } }
    public string Progress { get => _progress; private set { _progress = value; Changed(); } }
    public string Model { get => _model; private set { _model = value; Changed(); } }
    public string Vendor { get => _vendor; private set { _vendor = value; Changed(); } }
    public string Capacity { get => _capacity; private set { _capacity = value; Changed(); } }
    public string Kind { get => _kind; private set { _kind = value; Changed(); } }
    public string Bus { get => _bus; private set { _bus = value; Changed(); } }
    public string Serial { get => _serial; private set { _serial = value; Changed(); } }
    public string Letters { get => _letters; private set { _letters = value; Changed(); } }
    public string Temperature { get => _temperature; private set { _temperature = value; Changed(); } }
    public string Hours { get => _hours; private set { _hours = value; Changed(); } }
    public string Cycles { get => _cycles; private set { _cycles = value; Changed(); } }
    public string LifeRemaining { get => _lifeRemaining; private set { _lifeRemaining = value; Changed(); } }
    public string HealthText { get => _healthText; private set { _healthText = value; Changed(); } }
    public string Summary { get => _summary; private set { _summary = value; Changed(); } }
    public Brush HealthBrush { get => _healthBrush; private set { _healthBrush = value; Changed(); } }
    public string ActionUrgencyText { get => _actionUrgency; private set { _actionUrgency = value; Changed(); } }
    public string ActionHeadline { get => _actionHeadline; private set { _actionHeadline = value; Changed(); } }
    public Brush ActionBrush { get => _actionBrush; private set { _actionBrush = value; Changed(); } }
    public Brush ActionTextBrush { get => _actionTextBrush; private set { _actionTextBrush = value; Changed(); } }
    public Visibility ActionVisibility { get => _actionVisibility; private set { _actionVisibility = value; Changed(); } }
    public string NextCheckDate { get => _nextCheckDate; private set { _nextCheckDate = value; Changed(); } }
    public Visibility NextCheckVisibility { get => _nextCheckVisibility; private set { _nextCheckVisibility = value; Changed(); } }
    public Visibility AtaVisibility { get => _ataVisibility; private set { _ataVisibility = value; Changed(); } }
    public Visibility NvmeVisibility { get => _nvmeVisibility; private set { _nvmeVisibility = value; Changed(); } }
    public Visibility SmartUnavailableVisibility { get => _smartUnavailableVisibility; private set { _smartUnavailableVisibility = value; Changed(); } }
    public Visibility NoFindingsVisibility { get => _noFindingsVisibility; private set { _noFindingsVisibility = value; Changed(); } }
    public string SmartUnavailableText { get => _smartUnavailableText; private set { _smartUnavailableText = value; Changed(); } }

    public async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        Changed(nameof(CanRefresh)); Changed(nameof(CanStartScan));
        Progress = L.T("U017");
        Disks.Clear();
        SelectedDisk = null;
        try
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Progress = L.T("U018");
                Summary = Progress;
                return;
            }
            var disks = await Task.Run(() => DiskDiscovery.Discover().Select(SmartReader.Read).ToArray());
            foreach (var disk in disks) Disks.Add(new DiskChoice(disk));
            SelectedDisk = Disks.FirstOrDefault();
            Progress = Disks.Count == 0 ? L.T("U019") : L.F("U020", Disks.Count);
        }
        catch (Exception ex)
        {
            Progress = L.F("U021", ex.Message);
            Summary = Progress;
        }
        finally
        {
            _busy = false;
            Changed(nameof(CanRefresh)); Changed(nameof(CanStartScan));
        }
    }

    public async Task StartScanAsync(ScanMode mode)
    {
        if (!CanStartScan || SelectedDisk is null) return;
        var disk = SelectedDisk.Disk;
        LastScan = null;
        ResetScan();
        var layout = ReadOnlyScanner.Layout(disk.SizeBytes, disk.LogicalSectorSize);
        CreateGrid();
        var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        Changed(nameof(IsScanning)); Changed(nameof(CanSelectDisk)); Changed(nameof(CanRefresh)); Changed(nameof(CanStartScan));
        Progress = mode == ScanMode.Full ? L.T("U022") : L.T("U023");
        var report = new Progress<ScanProgress>(p => UpdateScan(p));
        try
        {
            var scan = await _scanner.RunAsync(disk, mode, report, cancellation.Token);
            await Dispatcher.Yield(DispatcherPriority.Background);
            _scannedDisk = disk;
            LastScan = scan;
            ScanPercent = scan.BlocksTotal == 0 ? "0%" : $"{100.0 * (scan.OkBlocks + scan.SlowBlocks + scan.ErrorBlocks) / scan.BlocksTotal:0.0}%";
            ScanCapacity = FormatBytes(scan.BytesScanned);
            ScanElapsed = FormatElapsed(scan.Elapsed);
            ScanErrors = scan.ErrorOffsets.Count == 1000 ? L.T("U024") : scan.ErrorOffsets.Count.ToString("N0");
            ScanSlow = scan.SlowBlocks.ToString("N0");
            ScanAvg = $"{scan.AvgLatencyMs:0.0} ms";
            ScanMax = $"{scan.MaxLatencyMs:0.0} ms";
            Progress = scan.AbortReason ?? (scan.Cancelled ? L.T("U025") : L.F("U026", layout.Blocks));
            Show(disk);
        }
        catch (Exception ex)
        {
            Progress = L.F("U027", ex.Message);
        }
        finally
        {
            _scanCancellation = null;
            cancellation.Dispose();
            Changed(nameof(IsScanning)); Changed(nameof(CanSelectDisk)); Changed(nameof(CanRefresh)); Changed(nameof(CanStartScan));
        }
    }

    public void StopScan() => _scanCancellation?.Cancel();

    private void ResetScan()
    {
        ScanGrid = null;
        ScanPercent = "0%"; ScanCapacity = "0.0 GB"; ScanElapsed = L.T("U028");
        ScanErrors = "0"; ScanSlow = "0"; ScanAvg = "0 ms"; ScanMax = "0 ms";
    }

    private void CreateGrid()
    {
        const int width = GridColumns * CellPixels, height = 40 * CellPixels;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var gridline = x % CellPixels == CellPixels - 1 || y % CellPixels == CellPixels - 1;
                pixels[i] = gridline ? (byte)255 : (byte)178;
                pixels[i + 1] = gridline ? (byte)255 : (byte)185;
                pixels[i + 2] = gridline ? (byte)255 : (byte)194;
                pixels[i + 3] = 255;
            }
        ScanGrid = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        ScanGrid.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    }

    private void UpdateScan(ScanProgress p)
    {
        if (!IsScanning) return;
        ScanPercent = p.BlocksTotal == 0 ? "0%" : $"{100.0 * p.CompletedBlocks / p.BlocksTotal:0.0}%";
        ScanCapacity = FormatBytes(p.BytesScanned);
        ScanElapsed = FormatElapsed(p.Elapsed);
        ScanErrors = p.ErrorCount.ToString("N0");
        ScanSlow = p.SlowCount.ToString("N0");
        ScanAvg = $"{p.AvgLatencyMs:0.0} ms";
        ScanMax = $"{p.MaxLatencyMs:0.0} ms";
        if (p.BlockState == BlockState.Untested || p.BlockIndex < 0 || ScanGrid is null) return;
        var color = p.BlockState switch
        {
            BlockState.Ok => Color.FromRgb(51, 170, 103),
            BlockState.Slow => Color.FromRgb(231, 185, 53),
            _ => Color.FromRgb(210, 70, 70)
        };
        var pixels = new byte[(CellPixels - 1) * (CellPixels - 1) * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255;
        }
        ScanGrid.WritePixels(new Int32Rect(p.BlockIndex % GridColumns * CellPixels,
            p.BlockIndex / GridColumns * CellPixels, CellPixels - 1, CellPixels - 1),
            pixels, (CellPixels - 1) * 4, 0);
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1_000_000_000.0:0.0} GB";

    private static string FormatElapsed(TimeSpan elapsed) => elapsed < TimeSpan.FromMinutes(1)
        ? L.F("U029", Math.Max(0, elapsed.TotalSeconds))
        : elapsed.ToString(@"hh\:mm\:ss");

    private void Show(PhysicalDiskInfo? disk)
    {
        Findings.Clear();
        ActionSteps.Clear();
        ActionVisibility = Visibility.Collapsed;
        NextCheckVisibility = Visibility.Collapsed;
        SmartRows.Clear();
        NvmeRows.Clear();
        NoFindingsVisibility = Visibility.Collapsed;
        Model = Value(disk?.Model);
        Vendor = Value(disk?.Vendor);
        Capacity = disk is { SizeBytes: > 0 } ? $"{disk.SizeBytes / 1_000_000_000.0:0.##} GB" : L.T("U030");
        Kind = disk?.Kind switch { DriveKind.Hdd => "HDD", DriveKind.SataSsd => "SSD", DriveKind.NvmeSsd => "NVMe SSD", _ => L.T("U031") };
        Bus = disk?.BusType is { } bus && bus != "Unknown" ? bus : L.T("U033");
        Serial = string.IsNullOrWhiteSpace(disk?.Serial) ? L.T("U034") : disk.Serial[..Math.Min(4, disk.Serial.Length)] + "••••";
        Letters = disk is { DriveLetters.Count: > 0 } ? string.Join(" ", disk.DriveLetters) : L.T("U035");
        if (disk is null)
        {
            Temperature = Hours = Cycles = LifeRemaining = L.T("U036");
            HealthText = L.T("U037"); HealthBrush = Brushes.Gray; Summary = L.T("U038");
            AtaVisibility = NvmeVisibility = Visibility.Collapsed;
            SmartUnavailableVisibility = Visibility.Visible;
            SmartUnavailableText = L.T("U039");
            return;
        }
        var diagnosis = HealthAnalyzer.Analyze(disk, LastScan);
        var plan = ActionAdvisor.Build(disk, diagnosis, LastScan);
        ActionUrgencyText = plan.UrgencyText;
        ActionHeadline = plan.Headline;
        ActionBrush = plan.Urgency switch
        {
            ActionUrgency.Immediate => Brushes.Firebrick,
            ActionUrgency.Soon => Brushes.Goldenrod,
            ActionUrgency.Routine => Brushes.ForestGreen,
            _ => Brushes.Gray
        };
        ActionTextBrush = plan.Urgency == ActionUrgency.Soon ? Brushes.Black : Brushes.White;
        for (var i = 0; i < plan.Steps.Count; i++)
            ActionSteps.Add(new ActionDisplayStep($"{i + 1}.", plan.Steps[i].Title, plan.Steps[i].Detail));
        if (plan.Urgency == ActionUrgency.Routine)
        {
            NextCheckDate = L.F("U040", DateTime.Now.AddMonths(ActionRules.FullCheckIntervalMonths));
            NextCheckVisibility = Visibility.Visible;
        }
        ActionVisibility = Visibility.Visible;
        var tempAttribute = disk.AtaSmart?.Get(0xC2) ?? disk.AtaSmart?.Get(0xBE);
        var temperature = diagnosis.TemperatureCelsius ?? disk.NvmeHealth?.TemperatureCelsius
            ?? (tempAttribute is null ? null : (int?)(tempAttribute.Raw & 0xFF));
        var hours = diagnosis.PowerOnHours ?? disk.NvmeHealth?.PowerOnHours ?? disk.AtaSmart?.Get(0x09)?.Raw;
        var cycles = diagnosis.PowerCycles ?? disk.NvmeHealth?.PowerCycles ?? disk.AtaSmart?.Get(0x0C)?.Raw;
        Temperature = temperature is { } t ? $"{t} °C" : L.T("U041");
        Hours = hours is { } h ? L.F("U042", h) : L.T("U043");
        Cycles = cycles is { } c ? L.F("U044", c) : L.T("U045");
        LifeRemaining = disk.Kind == DriveKind.Hdd ? L.T("U046")
            : diagnosis.LifeRemainingPercent is { } life ? $"{life}%" : L.T("U047");
        HealthText = diagnosis.StatusText;
        HealthBrush = diagnosis.Status switch { HealthStatus.Normal => Brushes.ForestGreen, HealthStatus.Caution => Brushes.DarkGoldenrod, HealthStatus.Danger => Brushes.Firebrick, _ => Brushes.Gray };
        Summary = !string.IsNullOrWhiteSpace(disk.HealthUnavailableReason) ? disk.HealthUnavailableReason : Value(diagnosis.Summary);
        foreach (var finding in diagnosis.Findings) Findings.Add(finding);
        NoFindingsVisibility = diagnosis.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (disk.AtaSmart is { } ata)
            foreach (var a in ata.Attributes) SmartRows.Add(new SmartRow($"{a.Id:X2}", a.Name, a.Current.ToString(), a.Worst.ToString(),
                a.Threshold?.ToString() ?? L.T("U052"), $"0x{a.Raw:X12}", a.Raw.ToString("N0")));
        if (disk.NvmeHealth is { } nvme) AddNvmeRows(nvme);
        AtaVisibility = disk.AtaSmart is null ? Visibility.Collapsed : Visibility.Visible;
        NvmeVisibility = disk.NvmeHealth is null ? Visibility.Collapsed : Visibility.Visible;
        SmartUnavailableVisibility = disk.AtaSmart is null && disk.NvmeHealth is null ? Visibility.Visible : Visibility.Collapsed;
        SmartUnavailableText = disk.HealthUnavailableReason ?? L.T("U053");
    }

    private void AddNvmeRows(NvmeHealthLog log)
    {
        void Add(string name, object value) => NvmeRows.Add(new NvmeRow(name, value.ToString() ?? L.T("U054")));
        Add(L.T("U055"), $"0x{log.CriticalWarning:X2}");
        Add(L.T("U056"), log.TemperatureCelsius is { } c ? $"{c} °C" : L.T("U057"));
        Add(L.T("U058"), $"{log.AvailableSpare}%");
        Add(L.T("U059"), $"{log.AvailableSpareThreshold}%");
        Add(L.T("U060"), $"{log.PercentageUsed}%");
        Add(L.T("U061"), FormatDataUnits(log.DataUnitsRead));
        Add(L.T("U062"), FormatDataUnits(log.DataUnitsWritten));
        Add(L.T("U063"), log.PowerCycles);
        Add(L.T("U064"), log.PowerOnHours);
        Add(L.T("U065"), log.UnsafeShutdowns);
        Add(L.T("U066"), log.MediaErrors);
        Add(L.T("U067"), log.ErrorLogEntries);
    }

    private static string FormatDataUnits(ulong units)
    {
        var bytes = units * 512_000m;
        return bytes >= 1_000_000_000_000m
            ? L.F("U068", units, bytes / 1_000_000_000_000m)
            : L.F("U069", units, bytes / 1_000_000_000m);
    }

    private static string Value(string? text) => string.IsNullOrWhiteSpace(text) ? L.T("U070") : text;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record DiskChoice(PhysicalDiskInfo Disk)
{
    public string Display => $"#{Disk.Index} " + (!string.IsNullOrWhiteSpace(Disk.Model) ? Disk.Model : L.T("U071")) + " " +
        (Disk.SizeBytes > 0 ? $"{Disk.SizeBytes / 1_000_000_000.0:0.##} GB" : L.T("U072")) + " " + string.Join(" ", Disk.DriveLetters);
}

public sealed record SmartRow(string Id, string Name, string Current, string Worst, string Threshold, string RawHex, string RawDecimal);
public sealed record NvmeRow(string Name, string Value);
public sealed record ActionDisplayStep(string Number, string Title, string Detail);
