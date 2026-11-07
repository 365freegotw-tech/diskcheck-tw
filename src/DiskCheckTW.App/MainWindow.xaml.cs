using DiskCheckTW.App;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DiskCheckTW.Core.Analysis;
using DiskCheckTW.Core;
using DiskCheckTW.Core.Models;
using DiskCheckTW.Core.Reporting;
using DiskCheckTW.Core.Scheduling;
using DiskCheckTW.App.Scheduling;

namespace DiskCheckTW.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private string? _savedReportPath;
    private bool _languageReady;

    // 語言切換：存到 %APPDATA%\DiskCheck TW\settings.json，重新啟動後套用（避免執行中的文字一半中一半英）。
    private void LanguageChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageReady) return;
        var language = LanguageChoice.SelectedIndex == 0 ? "zh-TW" : "en";
        if (language == S.Language.Name) return;
        try
        {
            AppLanguage.Save(language);
            MessageBox.Show(this, L.T("X075"), L.T("X076"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, L.T("X076"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        AboutVersion.Text = L.T("U073") + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? L.T("U074"));
        _languageReady = false;
        LanguageChoice.SelectedIndex = S.Language.Name == "zh-TW" ? 0 : 1;
        _languageReady = true;
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedDisk)) ClearSavedReport();
        };
        Loaded += async (_, _) => { LoadSchedule(); await _viewModel.RefreshAsync(); };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();

    private async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStartScan || _viewModel.SelectedDisk is null) return;
        var diagnosis = HealthAnalyzer.Analyze(_viewModel.SelectedDisk.Disk, _viewModel.LastScan);
        var warning = diagnosis.Status == HealthStatus.Danger ? L.T("U075") : "";
        var answer = MessageBox.Show(this, warning + L.T("U076"),
            L.T("U077"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        await _viewModel.StartScanAsync(QuickMode.IsChecked == true ? ScanMode.QuickSample : ScanMode.Full);
    }

    private void StopScan_Click(object sender, RoutedEventArgs e) => _viewModel.StopScan();

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStartScan || _viewModel.SelectedDisk is null) return;
        var disk = _viewModel.SelectedDisk.Disk;
        var nowTaipei = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")).DateTime;
        var invalid = Path.GetInvalidFileNameChars();
        var model = new string(disk.Model.Trim().Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray())
            .Trim('_', '.');
        if (model.Length > 50) model = model[..50].TrimEnd('_', '.');
        if (model.Length == 0) model = "Disk";
        var dialog = new SaveFileDialog
        {
            Title = L.T("U078"),
            Filter = L.T("U079"),
            DefaultExt = ".html",
            AddExtension = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = $"DiskCheck_{model}_{nowTaipei:yyyyMMdd_HHmm}.html"
        };
        if (dialog.ShowDialog(this) != true) return;
        ClearSavedReport();
        try
        {
            var scan = _viewModel.SelectedDiskScan;
            var diagnosis = HealthAnalyzer.Analyze(disk, scan);
            var context = new ReportContext
            {
                GeneratedAt = nowTaipei,
                OsDescription = GetOsDescription(),
                AppVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? L.T("U080"),
                MaskSerial = MaskSerialCheck.IsChecked == true
            };
            File.WriteAllText(dialog.FileName, ReportGenerator.GenerateHtml(disk, diagnosis, scan, context), new UTF8Encoding(false));
            _savedReportPath = dialog.FileName;
            ExportStatus.Text = L.T("U081");
            OpenReportButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ExportStatus.Text = L.F("U082", ex.Message);
        }
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (_savedReportPath is null) return;
        try { Process.Start(new ProcessStartInfo(_savedReportPath) { UseShellExecute = true }); }
        catch (Exception ex) { ExportStatus.Text = L.F("U083", ex.Message); }
    }

    private void OfficialSite_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(OfficialLinks.OfficialSite) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, L.F("U084", ex.Message)); }
    }

    private void OpenNotices_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md")) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, L.F("U085", ex.Message)); }
    }

    private void ClearSavedReport()
    {
        _savedReportPath = null;
        ExportStatus.Text = "";
        OpenReportButton.Visibility = Visibility.Collapsed;
    }

    private void LoadSchedule()
    {
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)DateTime.Today.DayOfWeek + 7) % 7;
        var nextSaturday = DateTime.Today.AddDays(daysUntilSaturday == 0 ? 7 : daysUntilSaturday);
        ScheduleDate.SelectedDate = nextSaturday;
        ScheduleNext.Text = L.T("U086");
        ScheduleLast.Text = L.T("U087");
        ScheduleControls.IsEnabled = ScheduleService.IsInstalled();
        ScheduleInstallNotice.Visibility = ScheduleControls.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
        if (!ScheduleControls.IsEnabled) return;
        try
        {
            var settings = ScheduleService.Load();
            if (settings is null) return;
            ScheduleEnabled.IsChecked = settings.Enabled;
            ScheduleFrequency.SelectedIndex = settings.IntervalMonths switch { 1 => 0, 6 => 2, _ => 1 };
            ScheduleDate.SelectedDate = settings.StartAt.Date;
            ScheduleTime.Text = settings.StartAt.ToString("HH:mm", CultureInfo.InvariantCulture);
            ScheduleMode.SelectedIndex = settings.FullScan ? 1 : 0;
            ShowScheduleState(settings);
        }
        catch (Exception ex) { ScheduleMessage.Text = L.F("U088", ex.Message); }
    }

    private void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (!ScheduleControls.IsEnabled) return;
        if (ScheduleDate.SelectedDate is not DateTime date ||
            !TimeSpan.TryParseExact(ScheduleTime.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var time))
        {
            ScheduleMessage.Text = L.T("U089");
            return;
        }
        var start = date.Date.Add(time);
        var enabled = ScheduleEnabled.IsChecked == true;
        var interval = ScheduleFrequency.SelectedIndex switch { 0 => 1, 2 => 6, _ => 3 };
        try
        {
            var old = ScheduleService.Load();
            var settings = new ScheduleSettings
            {
                Enabled = enabled,
                IntervalMonths = interval,
                StartAt = start,
                FullScan = ScheduleMode.SelectedIndex == 1,
                NextAt = enabled ? (start > DateTime.Now ? start : ScheduleRules.NextAfter(start, interval, DateTime.Now)) : default,
                LastAt = old?.LastAt,
                LastResult = old?.LastResult
            };
            ScheduleService.Apply(settings);
            ShowScheduleState(settings);
            ScheduleMessage.Text = enabled ? L.T("U090") : L.T("U091");
        }
        catch (Exception ex) { ScheduleMessage.Text = L.F("U092", ex.Message); }
    }

    private void ScheduleMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScheduleFullWarning is not null)
            ScheduleFullWarning.Visibility = ScheduleMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowScheduleState(ScheduleSettings settings)
    {
        ScheduleNext.Text = settings.Enabled ? L.F("U093", settings.NextAt) : L.T("U094");
        ScheduleLast.Text = settings.LastAt is DateTime last
            ? L.F("U095", last, settings.LastResult ?? L.T("U138"))
            : L.T("U096");
    }

    private static string GetOsDescription()
    {
        var description = RuntimeInformation.OSDescription;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string;
            var version = key?.GetValue("DisplayVersion") as string;
            var details = string.Join(" ", new[] { product, version }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return details.Length == 0 ? description : description + S.Paren(details);
        }
        catch (Exception) { return description; }
    }
}
