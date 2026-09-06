using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace GuguPet;

public static class CodexLaunchWatcherManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GuguPet.CodexWatcher";
    public const string WatcherStopEventName = @"Local\GuguPet.CodexWatcher.Stop";
    private const string WatcherExecutableName = "GuguPet.LaunchWatcher.exe";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value &&
               value.Contains(WatcherExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool EnsureCurrent()
    {
        if (!IsEnabled()) return false;
        InstallAndStartCurrentWatcher(forceRestart: false);
        return true;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            StopRunningWatchers();
            return;
        }

        InstallAndStartCurrentWatcher(forceRestart: true, key);
    }

    public static bool IsCodexDesktopRunning()
    {
        var processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            return processes.Any(process =>
            {
                try { return process.MainWindowHandle != IntPtr.Zero; }
                catch { return false; }
            });
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    public static void LaunchPet()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(LocalizationService.T("无法获取咕嘎程序路径。"));
        Process.Start(new ProcessStartInfo(executable, "--codex-startup")
        {
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });
    }

    public static void SignalWatcherToStop()
    {
        try
        {
            using var stopEvent = EventWaitHandle.OpenExisting(WatcherStopEventName);
            stopEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    private static void InstallAndStartCurrentWatcher(bool forceRestart, RegistryKey? openRunKey = null)
    {
        var petExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException(LocalizationService.T("无法获取咕嘎程序路径。"));
        var sourceWatcher = Path.Combine(AppContext.BaseDirectory, WatcherExecutableName);
        if (!File.Exists(sourceWatcher))
            throw new FileNotFoundException(LocalizationService.T("缺少咕嘎启动监听器。"), sourceWatcher);

        var installedWatcher = AppPaths.LauncherExecutablePath;
        var expectedCommand = BuildRegistrationCommand(installedWatcher, petExecutable);
        var registrationMatches = false;
        using (var readKey = Registry.CurrentUser.OpenSubKey(RunKey, false))
        {
            registrationMatches = string.Equals(
                readKey?.GetValue(ValueName) as string,
                expectedCommand,
                StringComparison.OrdinalIgnoreCase);
        }

        if (!forceRestart && registrationMatches &&
            WatcherBinaryMatches(sourceWatcher, installedWatcher) &&
            IsWatcherRunningFrom(installedWatcher))
            return;

        StopRunningWatchers();
        InstallWatcherBinary(sourceWatcher, installedWatcher);

        var ownsKey = openRunKey is null;
        var key = openRunKey ?? Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException(LocalizationService.T("无法写入咕嘎启动项。"));
        try
        {
            key.SetValue(ValueName, expectedCommand, RegistryValueKind.String);
        }
        finally
        {
            if (ownsKey) key.Dispose();
        }

        try
        {
            Process.Start(new ProcessStartInfo(installedWatcher)
            {
                Arguments = $"--pet \"{petExecutable}\"",
                UseShellExecute = true,
                WorkingDirectory = AppPaths.LauncherDirectory
            });
            if (!WaitForWatcher(installedWatcher, TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException(LocalizationService.T("咕嘎启动监听器未能接管当前版本。"));
        }
        catch
        {
            try
            {
                using var cleanupKey = Registry.CurrentUser.CreateSubKey(RunKey, true);
                cleanupKey.DeleteValue(ValueName, false);
            }
            catch { }
            throw;
        }
    }

    private static string BuildRegistrationCommand(string watcher, string pet) =>
        $"\"{watcher}\" --pet \"{pet}\"";

    private static bool WatcherBinaryMatches(string source, string installed)
    {
        if (!File.Exists(installed)) return false;
        var sourceFile = new FileInfo(source);
        var installedFile = new FileInfo(installed);
        if (sourceFile.Length != installedFile.Length) return false;
        return string.Equals(
            FileVersionInfo.GetVersionInfo(source).FileVersion,
            FileVersionInfo.GetVersionInfo(installed).FileVersion,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void InstallWatcherBinary(string source, string destination)
    {
        Directory.CreateDirectory(AppPaths.LauncherDirectory);
        var temporary = destination + ".new";
        try
        {
            File.Copy(source, temporary, true);
            File.Move(temporary, destination, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private static void StopRunningWatchers()
    {
        SignalWatcherToStop();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            var running = GetWatcherProcesses();
            if (running.Count == 0) return;
            foreach (var process in running) process.Dispose();
            Thread.Sleep(100);
        }

        foreach (var process in GetWatcherProcesses())
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
                catch { }
            }
        }
    }

    private static bool WaitForWatcher(string expectedPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsWatcherRunningFrom(expectedPath)) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    private static bool IsWatcherRunningFrom(string expectedPath)
    {
        var expected = Path.GetFullPath(expectedPath);
        foreach (var process in GetWatcherProcesses())
        {
            using (process)
            {
                try
                {
                    var actual = process.MainModule?.FileName;
                    if (actual is not null && string.Equals(
                            Path.GetFullPath(actual), expected, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
        }
        return false;
    }

    private static List<Process> GetWatcherProcesses() =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(WatcherExecutableName)).ToList();
}
