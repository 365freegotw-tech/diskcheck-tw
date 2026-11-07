using DiskCheckTW.Core;
using DiskCheckTW.App;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DiskCheckTW.App.Hardware;
using DiskCheckTW.Core.Analysis;
using DiskCheckTW.Core.Models;
using DiskCheckTW.Core.Reporting;
using DiskCheckTW.Core.Scheduling;

namespace DiskCheckTW.App.Scheduling;

internal sealed class ScheduledWindow : Window
{
    private readonly StackPanel _content = new() { Margin = new Thickness(20) };
    private readonly CancellationTokenSource _cancel = new();
    private bool _running;
    private bool _done;
    private readonly string _reports = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "DiskCheck TW", "報告");

    internal ScheduledWindow()
    {
        Title = L.T("U097");
        Width = 510; Height = 320; MinWidth = 420; MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft JhengHei UI,Segoe UI");
        Content = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += async (_, _) => await BeginAsync();
        Closing += (_, e) => { if (_running) { _cancel.Cancel(); e.Cancel = true; } };
    }

    private async Task BeginAsync()
    {
        ScheduleSettings? settings;
        try { settings = ScheduleService.Load(); }
        catch (Exception ex) { Error(L.F("U098", ex.Message)); return; }
        if (settings is not { Enabled: true } || settings.NextAt == default || DateTime.Now < settings.NextAt)
        {
            Close();
            return;
        }
        if (ScheduleRules.IsMissed(DateTime.Now, settings.NextAt))
        {
            var choice = await AskMissedAsync(settings.NextAt);
            if (choice == 1) { Close(); return; }
            if (choice == 2)
            {
                settings.NextAt = ScheduleRules.NextAfter(settings.StartAt, settings.IntervalMonths, DateTime.Now);
                try { ScheduleService.Save(settings); Close(); }
                catch (Exception ex) { Error(L.F("U099", ex.Message)); }
                return;
            }
        }
        await RunAsync(settings);
    }

    // 0=立即檢測，1=稍後，2=略過。
    private Task<int> AskMissedAsync(DateTime due)
    {
        _content.Children.Clear();
        _content.Children.Add(Label(L.F("U100", due), 18));
        var row = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
        _content.Children.Add(row);
        var answer = new TaskCompletionSource<int>();
        foreach (var (title, value) in new[] { (L.T("U101"), 0), (L.T("U102"), 1), (L.T("U103"), 2) })
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 6, 10, 6) };
            button.Click += (_, _) => answer.TrySetResult(value);
            row.Children.Add(button);
        }
        return answer.Task;
    }

    private async Task RunAsync(ScheduleSettings settings)
    {
        _content.Children.Clear();
        var title = Label(L.T("U104"), 20);
        var progress = Label(L.T("U105"), 15);
        _content.Children.Add(title);
        _content.Children.Add(progress);
        var cancelButton = new Button { Content = L.T("U106"), Width = 110, Height = 34, HorizontalAlignment = HorizontalAlignment.Left };
        cancelButton.Click += (_, _) => _cancel.Cancel();
        _content.Children.Add(cancelButton);
        _running = true;
        var results = new List<(string Model, HealthStatus Status, string StatusText, string Headline)>();
        var started = DateTime.Now;
        try
        {
            Directory.CreateDirectory(_reports);
            var disks = await Task.Run(DiskDiscovery.Discover);
            var scanner = new ReadOnlyScanner();
            foreach (var raw in disks)
            {
                if (_cancel.IsCancellationRequested) break;
                progress.Text = L.F("U107", raw.Model);
                var disk = await Task.Run(() => SmartReader.Read(raw));
                var report = new Progress<ScanProgress>(p => progress.Text =
                    $"{disk.Model}{S.Colon}{(p.BlocksTotal == 0 ? 0 : 100.0 * p.CompletedBlocks / p.BlocksTotal):0.0}%");
                var scan = await scanner.RunAsync(disk, settings.FullScan ? ScanMode.Full : ScanMode.QuickSample, report, _cancel.Token);
                var diagnosis = HealthAnalyzer.Analyze(disk, scan);
                var plan = ActionAdvisor.Build(disk, diagnosis, scan);
                var nowTaipei = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")).DateTime;
                var context = new ReportContext
                {
                    GeneratedAt = nowTaipei,
                    OsDescription = RuntimeInformation.OSDescription,
                    AppVersion = typeof(ScheduledWindow).Assembly.GetName().Version?.ToString(3) ?? L.T("U108"),
                    MaskSerial = true
                };
                var name = SafeName(disk.Model);
                var path = Path.Combine(_reports, $"DiskCheck_{name}_{nowTaipei:yyyyMMdd_HHmm}.html");
                for (var copy = 2; File.Exists(path); copy++)
                    path = Path.Combine(_reports, $"DiskCheck_{name}_{nowTaipei:yyyyMMdd_HHmm}_{copy}.html");
                File.WriteAllText(path, ReportGenerator.GenerateHtml(disk, diagnosis, scan, context), new UTF8Encoding(false));
                results.Add(scan.Cancelled || scan.AbortReason is not null
                    ? (disk.Model, HealthStatus.InsufficientData, L.T("U109"), scan.AbortReason ?? L.T("U110"))
                    : (disk.Model, diagnosis.Status, diagnosis.StatusText, plan.Headline));
                if (scan?.Cancelled == true) break;
            }
            settings.LastAt = started;
            settings.LastResult = _cancel.IsCancellationRequested ? L.F("U111", results.Count) :
                results.Count == 0 ? L.T("U112") :
                string.Format(L.T("U113"), results.Count, string.Join(S.ListSep, results.Select(r => $"{r.Model}{S.Colon}{r.StatusText}")));
            if (!_cancel.IsCancellationRequested)
                settings.NextAt = ScheduleRules.NextAfter(settings.StartAt, settings.IntervalMonths, DateTime.Now);
            ScheduleService.Save(settings);
            ShowResults(results, settings.LastResult);
        }
        catch (Exception ex)
        {
            settings.LastAt = started;
            settings.LastResult = L.F("U114", ex.Message);
            try { ScheduleService.Save(settings); }
            catch { /* 原錯誤仍顯示給使用者 */ }
            Error(settings.LastResult);
        }
        finally { _running = false; }
    }

    private void ShowResults(List<(string Model, HealthStatus Status, string StatusText, string Headline)> results, string summary)
    {
        _done = true;
        _content.Children.Clear();
        _content.Children.Add(Label(L.T("U115"), 20));
        foreach (var result in results)
        {
            var color = result.Status switch
            {
                HealthStatus.Normal => Brushes.ForestGreen,
                HealthStatus.Caution => Brushes.DarkGoldenrod,
                HealthStatus.Danger => Brushes.Firebrick,
                _ => Brushes.DimGray
            };
            _content.Children.Add(new TextBlock
            {
                Text = $"{result.Model}{S.Bar}{result.StatusText}{S.Bar}{result.Headline}",
                Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 2), FontSize = 15
            });
        }
        _content.Children.Add(Label(summary, 13));
        var row = new WrapPanel { Margin = new Thickness(0, 15, 0, 0) };
        _content.Children.Add(row);
        AddButton(row, L.T("U116"), () =>
        {
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        });
        AddButton(row, L.T("U117"), () => Process.Start(new ProcessStartInfo(_reports) { UseShellExecute = true }));
        if (results.Count > 0 && !_cancel.IsCancellationRequested && results.All(r => r.Status == HealthStatus.Normal))
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timer.Tick += (_, _) => { timer.Stop(); if (_done) Close(); };
            timer.Start();
        }
    }

    private void Error(string message)
    {
        _content.Children.Clear();
        _content.Children.Add(Label(message, 16));
        var row = new WrapPanel { Margin = new Thickness(0, 15, 0, 0) };
        _content.Children.Add(row);
        AddButton(row, L.T("U118"), Close);
    }

    private static TextBlock Label(string text, double size) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
    };

    private static void AddButton(WrapPanel row, string title, Action action)
    {
        var button = new Button { Content = title, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 8) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, L.T("U119")); } };
        row.Children.Add(button);
    }

    private static string SafeName(string model)
    {
        var name = new string(model.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray()).Trim('_', '.');
        return name.Length == 0 ? "Disk" : name.Length > 50 ? name[..50].TrimEnd('_', '.') : name;
    }
}
