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

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            SignalWatcherToStop();
            return;
        }

        var petExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException(LocalizationService.T("无法获取咕嘎程序路径。"));
        var watcherExecutable = Path.Combine(
            Path.GetDirectoryName(petExecutable)
                ?? throw new InvalidOperationException(LocalizationService.T("无法获取咕嘎程序目录。")),
            WatcherExecutableName);
        if (!File.Exists(watcherExecutable))
            throw new FileNotFoundException(LocalizationService.T("缺少咕嘎启动监听器。"), watcherExecutable);

        key.SetValue(ValueName, $"\"{watcherExecutable}\"", RegistryValueKind.String);
        try
        {
            Process.Start(new ProcessStartInfo(watcherExecutable)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch
        {
            key.DeleteValue(ValueName, false);
            throw;
        }
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

    private static void SignalWatcherToStop()
    {
        try
        {
            using var stopEvent = EventWaitHandle.OpenExisting(WatcherStopEventName);
            stopEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }
}
