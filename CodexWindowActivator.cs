using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GuguPet;

public static class CodexWindowActivator
{
    private const int SwRestore = 9;
    private const string CodexAppId = "OpenAI.Codex_2p2nqsd0c76g0!App";

    public static bool ActivateOrLaunch()
    {
        var window = FindCodexWindow();
        if (window != IntPtr.Zero)
            return ActivateExistingWindow(window);

        try
        {
            Process.Start(new ProcessStartInfo(
                "explorer.exe",
                $"shell:AppsFolder\\{CodexAppId}")
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool ActivateFirstVisibleProcessWindow(string processName)
    {
        var result = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (!process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    return true;
                result = window;
                return false;
            }
            catch
            {
                return true;
            }
        }, IntPtr.Zero);
        return result != IntPtr.Zero && ActivateExistingWindow(result);
    }

    private static bool ActivateExistingWindow(IntPtr window)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                       AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            ShowWindow(window, SwRestore);
            BringWindowToTop(window);
            SetForegroundWindow(window);
            SetFocus(window);
            return !IsIconic(window);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static IntPtr FindCodexWindow()
    {
        var result = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (!process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))
                    return true;

                var title = new StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                var titleText = title.ToString();
                if (!string.IsNullOrWhiteSpace(titleText) &&
                    !titleText.Contains("Codex", StringComparison.OrdinalIgnoreCase) &&
                    !titleText.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase))
                    return true;

                result = window;
                return false;
            }
            catch
            {
                return true;
            }
        }, IntPtr.Zero);
        return result;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);
}
