using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GuguPet.LaunchWatcher;

internal static class Program
{
    private const string MutexName = @"Local\GuguPet.CodexWatcher";
    private const string StopEventName = @"Local\GuguPet.CodexWatcher.Stop";
    private const string PetExecutableName = "GuguPet.exe";
    private const int HshellWindowCreated = 1;
    private const int HshellWindowDestroyed = 2;
    private const int HshellWindowActivated = 4;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;

    private static readonly WindowProcedureCallback WindowProcedureDelegate = HandleWindowMessage;
    private static uint _shellHookMessage;
    private static IntPtr _codexWindow;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew) return;

        var instance = GetModuleHandle(null);
        var className = $"GuguPet.CodexWindowEventSink.{Environment.ProcessId}";
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Instance = instance,
            ClassName = className,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate)
        };
        if (RegisterClassEx(ref windowClass) == 0) return;

        var window = CreateWindowEx(
            0,
            className,
            className,
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            UnregisterClass(className, instance);
            return;
        }

        _shellHookMessage = RegisterWindowMessage("SHELLHOOK");
        if (_shellHookMessage == 0 || !RegisterShellHookWindow(window))
        {
            DestroyWindow(window);
            UnregisterClass(className, instance);
            return;
        }

        using var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName);
        var stopThread = new Thread(() =>
        {
            stopEvent.WaitOne();
            PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
        })
        {
            IsBackground = true,
            Name = "GuguPet watcher stop signal"
        };
        stopThread.Start();

        _codexWindow = FindCodexWindow();
        if (_codexWindow != IntPtr.Zero) LaunchPet();

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        DeregisterShellHookWindow(window);
        DestroyWindow(window);
        UnregisterClass(className, instance);
    }

    private static IntPtr HandleWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _shellHookMessage)
        {
            var eventCode = unchecked((int)wParam.ToInt64()) & 0x7fff;
            if (eventCode is HshellWindowCreated or HshellWindowActivated)
            {
                if (_codexWindow == IntPtr.Zero && IsCodexWindow(lParam))
                {
                    _codexWindow = lParam;
                    LaunchPet();
                }
            }
            else if (eventCode == HshellWindowDestroyed && lParam == _codexWindow)
            {
                _codexWindow = FindCodexWindow();
            }
            return IntPtr.Zero;
        }

        if (message == WmClose)
        {
            DestroyWindow(window);
            return IntPtr.Zero;
        }
        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private static void LaunchPet()
    {
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, PetExecutableName);
            if (!File.Exists(executable)) return;
            Process.Start(new ProcessStartInfo(executable, "--codex-startup")
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch { }
    }

    private static IntPtr FindCodexWindow()
    {
        var processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        return process.MainWindowHandle;
                }
                catch { }
            }
            return IntPtr.Zero;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static bool IsCodexWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window)) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            return string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedureCallback(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out WindowMessage message, IntPtr window, uint minFilter, uint maxFilter);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref WindowMessage message);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref WindowMessage message);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterShellHookWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DeregisterShellHookWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string messageName);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
