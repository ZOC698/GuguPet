using System.Diagnostics;
using System.IO;

namespace GuguPet;

public static class ActivityWindowActivator
{
    public static bool Activate(string? source) =>
        source?.Equals("dsh", StringComparison.OrdinalIgnoreCase) == true
            ? DshWindowActivator.ActivateOrLaunch()
            : CodexWindowActivator.ActivateOrLaunch();
}

public static class DshWindowActivator
{
    public static bool Activate() =>
        CodexWindowActivator.ActivateFirstVisibleProcessWindow("Gugu DSH");

    public static bool ActivateOrLaunch()
    {
        if (Activate()) return true;

        var launchTarget = FindLaunchTarget();
        if (launchTarget is null) return false;
        try
        {
            return Process.Start(new ProcessStartInfo(launchTarget)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(launchTarget) ?? AppContext.BaseDirectory
            }) is not null;
        }
        catch
        {
            return false;
        }
    }

    internal static string? FindLaunchTarget()
    {
        var shortcutNames = new[]
        {
            "咕嘎 DSH.lnk",
            "Gugu DSH.lnk",
            "DeepSeek Harness.lnk",
            "DeepSeekHarnessGUI.lnk"
        };
        var shortcutDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        foreach (var directory in shortcutDirectories.Where(Directory.Exists))
            foreach (var name in shortcutNames)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) return path;
            }

        var localExecutables = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Gugu DSH.exe"),
            Path.Combine(AppContext.BaseDirectory, "DeepSeekHarnessGUI.exe")
        };
        return localExecutables.FirstOrDefault(File.Exists);
    }
}
