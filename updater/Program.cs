using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GuguPet.Updater;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = ParseArguments(args);
        var noRestart = options.ContainsKey("no-restart");
        var watcherEnabled = IsWatcherEnabled();
        try
        {
            var package = RequirePath(options, "package", mustExist: true);
            var target = RequirePath(options, "target", mustExist: true);
            ValidateTarget(target);
            var processId = int.TryParse(options.GetValueOrDefault("pid"), out var parsedPid) ? parsedPid : 0;
            WaitForProcess(processId);
            Install(package, target);
            if (!noRestart) Restart(target, watcherEnabled);
        }
        catch (Exception exception)
        {
            Log($"FAILED: {exception}");
            if (!noRestart)
                MessageBox.Show(
                    $"GuguPet 更新失败，旧版本已尽量保留。\n\n{exception.Message}",
                    "GuguPet Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    private static void Install(string package, string target)
    {
        var parent = Directory.GetParent(target)?.FullName
            ?? throw new InvalidOperationException("Cannot update a filesystem root.");
        var token = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Environment.ProcessId}";
        var staging = Path.Combine(parent, $".gugupet-update-{token}");
        var backup = target + $".backup-{DateTime.UtcNow:yyyyMMddHHmmss}";
        Directory.CreateDirectory(staging);
        ZipFile.ExtractToDirectory(package, staging, overwriteFiles: true);
        ValidatePackage(staging);

        Retry(() => Directory.Move(target, backup), "The running GuguPet directory is still locked.");
        try
        {
            Directory.Move(staging, target);
            Log($"Installed update into {target}; backup={backup}");
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }
    }

    private static void Restart(string target, bool watcherEnabled)
    {
        var pet = Path.Combine(target, "GuguPet.exe");
        var watcher = Path.Combine(target, "GuguPet.LaunchWatcher.exe");
        if (watcherEnabled && File.Exists(watcher))
        {
            UpdateWatcherRegistration(watcher);
            Process.Start(new ProcessStartInfo(watcher) { UseShellExecute = true, WorkingDirectory = target });
        }
        Process.Start(new ProcessStartInfo(pet, "--skip-startup-animation")
        {
            UseShellExecute = true,
            WorkingDirectory = target
        });
    }

    private static bool IsWatcherEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("GuguPet.CodexWatcher") is string value &&
                   value.Contains("GuguPet.LaunchWatcher.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void UpdateWatcherRegistration(string watcher)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key.SetValue("GuguPet.CodexWatcher", $"\"{watcher}\"", RegistryValueKind.String);
        }
        catch { }
    }

    private static void WaitForProcess(int processId)
    {
        if (processId <= 0) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(30000);
        }
        catch (ArgumentException) { }
    }

    private static void Retry(Action action, string message)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException exception)
            {
                last = exception;
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException exception)
            {
                last = exception;
                Thread.Sleep(500);
            }
        }
        throw new IOException(message, last);
    }

    private static void ValidateTarget(string target)
    {
        var root = Path.GetPathRoot(target);
        if (string.Equals(Path.TrimEndingDirectorySeparator(target), Path.TrimEndingDirectorySeparator(root ?? ""), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to update a filesystem root.");
        if (!File.Exists(Path.Combine(target, "GuguPet.exe")))
            throw new InvalidDataException("The target directory is not a GuguPet installation.");
    }

    private static void ValidatePackage(string staging)
    {
        foreach (var name in new[] { "GuguPet.exe", "GuguPet.dll", "GuguPet.LaunchWatcher.exe", "GuguPet.Updater.exe" })
            if (!File.Exists(Path.Combine(staging, name)))
                throw new InvalidDataException($"The update package is missing {name}.");
    }

    private static string RequirePath(Dictionary<string, string?> options, string name, bool mustExist)
    {
        var value = options.GetValueOrDefault(name);
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Missing --{name}.");
        var fullPath = Path.GetFullPath(value);
        if (mustExist && !File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException($"Path does not exist: {fullPath}");
        return fullPath;
    }

    private static Dictionary<string, string?> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[index][2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : null;
            result[key] = value;
        }
        return result;
    }

    private static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GuguPet");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "update.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch { }
    }
}
