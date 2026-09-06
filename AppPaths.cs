using System.IO;

namespace GuguPet;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GuguPet");

    public static string BridgeStatePath => Path.Combine(DataDirectory, "bridge-state.json");

    public static string UpdatesDirectory => Path.Combine(DataDirectory, "updates");

    public static string LauncherDirectory => Path.Combine(DataDirectory, "Launcher");

    public static string LauncherExecutablePath => Path.Combine(
        LauncherDirectory,
        "GuguPet.LaunchWatcher.exe");

    public static string CodexSessionsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        "sessions");
}
