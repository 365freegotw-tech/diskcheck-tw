using DiskCheckTW.App;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using DiskCheckTW.Core.Scheduling;

namespace DiskCheckTW.App.Scheduling;

internal static class ScheduleService
{
    internal const string TaskName = @"DiskCheck TW\定期檢測";
    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DiskCheck TW");
    internal static readonly string FilePath = Path.Combine(Folder, "schedule.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static bool IsInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"Software\DiskCheck TW");
        var dir = key?.GetValue("InstallDir") as string;
        return !string.IsNullOrWhiteSpace(dir) &&
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)), StringComparison.OrdinalIgnoreCase);
    }

    internal static ScheduleSettings? Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<ScheduleSettings>(File.ReadAllText(FilePath, Encoding.UTF8)) : null;

    internal static void Save(ScheduleSettings value)
    {
        Directory.CreateDirectory(Folder);
        var temp = Path.Combine(Folder, $"schedule.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, FilePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static void Apply(ScheduleSettings settings)
    {
        if (!settings.Enabled)
        {
            if (!RunTask(true, "/Delete", "/TN", TaskName, "/F") && RunTask(true, "/Query", "/TN", TaskName))
                throw new InvalidOperationException(L.T("U120"));
            Save(settings);
            return;
        }
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException(L.T("U121"));
        var xmlPath = Path.Combine(Path.GetTempPath(), $"DiskCheckTW-{Guid.NewGuid():N}.xml");
        try
        {
            var xml = ScheduleRules.TaskXml(settings, sid, Path.Combine(AppContext.BaseDirectory, "DiskCheckTW.App.exe"));
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            RunTask(false, "/Create", "/XML", xmlPath, "/TN", TaskName, "/F");
            Save(settings);
        }
        finally { if (File.Exists(xmlPath)) File.Delete(xmlPath); }
    }

    private static bool RunTask(bool ignoreFailure, params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var error = process.StandardError.ReadToEnd();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && !ignoreFailure)
            throw new InvalidOperationException(L.F("U122", error.Trim(), output.Trim()));
        return process.ExitCode == 0;
    }
}
