using System.Threading;
using System.Windows;
using DiskCheckTW.App.Scheduling;

namespace DiskCheckTW.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppLanguage.Apply();
        base.OnStartup(e);
        _singleInstance = new Mutex(true, @"Local\DiskCheckTW.App", out var first);
        _ownsMutex = first;
        if (!first) { Shutdown(); return; }
        Window window = e.Args.Contains("--scheduled", StringComparer.OrdinalIgnoreCase)
            ? new ScheduledWindow() : new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstance is not null)
        {
            if (_ownsMutex) _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}

