using System.IO;
using System.Collections.Specialized;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace GuguPet;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private PetWindow? _petWindow;
    private ControlWindow? _controlWindow;
    private StatusBubbleWindow? _statusBubble;
    private StatusBubbleWindow? _dshStatusBubble;
    private BridgeStateWatcher? _bridge;
    private CodexActivityWatcher? _codexActivity;
    private DshActivityWatcher? _dshActivity;
    private CodexActivityState? _lastCodexActivity;
    private CodexActivityState? _lastDshActivity;
    private TrayIconManager? _tray;
    private StartupPeepholeWindow? _startupPeephole;
    private AppSettings _settings = new();
    private bool _demoCapture;
    private DispatcherTimer? _demoTimer;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _demoCapture = e.Args.Contains("--demo-capture", StringComparer.OrdinalIgnoreCase);
        var mutexName = _demoCapture
            ? @"Local\GuguPet.DemoCapture"
            : @"Local\GuguPet.SingleInstance";
        _singleInstance = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Directory.CreateDirectory(AppPaths.DataDirectory);
        _settings = _demoCapture ? new AppSettings() : SettingsStore.Load();
        LocalizationService.Initialize(_settings.Language);
        _settings.StartWithWindows = StartupManager.IsEnabled();
        SynchronizeCodexStartupIntegration();
        var bubblePreview = e.Args.FirstOrDefault(arg =>
            arg.StartsWith("--preview-bubble=", StringComparison.OrdinalIgnoreCase));

        _petWindow = new PetWindow();
        ApplyPetSettings(_settings);
        if (_demoCapture)
            _petWindow.ConfigureDemoCapture();
        _statusBubble = new StatusBubbleWindow(_petWindow, preferRight: true)
        {
            ActivityBubbleEnabled = _settings.ActivityBubbleEnabled,
            DisplaySeconds = _settings.BubbleDisplaySeconds
        };
        _dshStatusBubble = new StatusBubbleWindow(_petWindow, preferRight: false)
        {
            ActivityBubbleEnabled = _settings.ActivityBubbleEnabled,
            DisplaySeconds = _settings.BubbleDisplaySeconds
        };
        _controlWindow = new ControlWindow(_petWindow, _statusBubble, _dshStatusBubble, _settings);
        _petWindow.OpenControlsRequested += (_, _) => _controlWindow.ShowAndActivate();
        _petWindow.NewCodexTaskRequested += (_, _) =>
        {
            CodexWindowActivator.ActivateOrLaunch();
            _statusBubble.ShowNotice(
                LocalizationService.T("新建 Codex 任务"),
                LocalizationService.T("Codex 已打开，请在侧边栏选择“新建任务”。"));
        };
        _petWindow.OpenDshRequested += (_, _) =>
        {
            if (!DshWindowActivator.ActivateOrLaunch())
                _dshStatusBubble.ShowNotice(
                    LocalizationService.T("未找到 DSH 启动入口"),
                    LocalizationService.T("请先创建桌面快捷方式“咕嘎 DSH”，或把 DSH 放在咕嘎目录中。"));
        };
        _petWindow.FilesDropped += PrepareFilesForCodex;
        _petWindow.SettingsChanged += (_, _) => QueueSave();
        _statusBubble.ActivityRequested += source => ActivityWindowActivator.Activate(source);
        _dshStatusBubble.ActivityRequested += source => ActivityWindowActivator.Activate(source);
        _controlWindow.ExitRequested += (_, _) => ExitApplication();
        _controlWindow.SettingsChanged += (_, _) => QueueSave();
        _controlWindow.StartupChanged += (_, enabled) => SetStartup(enabled);
        _controlWindow.CodexStartupChanged += (_, enabled) => SetCodexStartup(enabled);
        _controlWindow.PreviewStartupRequested += (_, _) => PlayStartupAnimation(initialLaunch: false, showControlAfter: false);

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSettings();
        };

        _tray = new TrayIconManager(
            () => Dispatcher.Invoke(_controlWindow.ShowAndActivate),
            () => Dispatcher.Invoke(() => _petWindow.PlayTransient("cookie", autoClear: true)),
            enabled => Dispatcher.Invoke(() =>
            {
                _petWindow.AutoRoam = enabled;
                _controlWindow.SetAutoRoam(enabled);
                QueueSave();
            }),
            enabled => Dispatcher.Invoke(() => SetStartup(enabled)),
            () => Dispatcher.Invoke(ExitApplication),
            _settings.AutoRoam,
            _settings.StartWithWindows);

        if (!_demoCapture)
        {
            _bridge = new BridgeStateWatcher(AppPaths.BridgeStatePath, state =>
            {
                Dispatcher.Invoke(() =>
                {
                    _petWindow.SetBaseState(state.State);
                    _controlWindow.UpdateBridgeStatus(state);
                });
            });
        }

        if (bubblePreview is null && !_demoCapture)
        {
            _codexActivity = new CodexActivityWatcher(AppPaths.CodexSessionsDirectory, state =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _lastCodexActivity = state;
                    if (_petWindow.IsVisible)
                        _statusBubble.UpdateActivity(state);
                    PublishMergedActivity();
                });
            });
            _dshActivity = new DshActivityWatcher(state =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _lastDshActivity = state;
                    if (_petWindow.IsVisible)
                        _dshStatusBubble.UpdateActivity(state);
                    PublishMergedActivity();
                });
            });
        }

        var showControlAfterStartup = !_demoCapture &&
                                      (_settings.ShowControlPanelOnLaunch ||
                                       e.Args.Contains("--show-control", StringComparer.OrdinalIgnoreCase));
        if (_demoCapture)
        {
            ShowMainWindows(showControl: false, playWave: false);
            StartDemoCaptureSequence();
        }
        else if (_settings.StartupAnimationEnabled &&
            !e.Args.Contains("--skip-startup-animation", StringComparer.OrdinalIgnoreCase))
            PlayStartupAnimation(initialLaunch: true, showControlAfter: showControlAfterStartup);
        else
            ShowMainWindows(showControlAfterStartup, playWave: true);

        if (bubblePreview is not null)
            ScheduleBubblePreview(bubblePreview.Split('=', 2)[1]);
    }

    private void PublishMergedActivity()
    {
        if (_petWindow is null || _controlWindow is null || _statusBubble is null || _dshStatusBubble is null) return;
        var state = ActivityStateMerger.Merge(_lastCodexActivity, _lastDshActivity);
        _controlWindow.UpdateCodexStatus(state);
        if (!_controlWindow.CodexSyncEnabled) return;

        _petWindow.SetBaseState(state.State);
        if (state.State == "running")
        {
            var activeBubble = state.Source.Equals("dsh", StringComparison.OrdinalIgnoreCase)
                ? _dshStatusBubble
                : _statusBubble;
            _petWindow.AcknowledgeProgress(
                activeBubble.Left + activeBubble.ActualWidth / 2,
                activeBubble.Top + activeBubble.ActualHeight / 2);
        }
    }

    private void StartDemoCaptureSequence()
    {
        if (_petWindow is null) return;
        var states = new[]
        {
            "waving", "jumping", "thinking-star", "guitar",
            "cookie", "celebrate-cheer", "celebrate-dance", "sleep-prone"
        };
        var index = 0;
        _petWindow.PlayTransient(states[index], autoClear: false);
        _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _demoTimer.Tick += (_, _) =>
        {
            if (_petWindow is null) return;
            index = (index + 1) % states.Length;
            _petWindow.PlayTransient(states[index], autoClear: false);
        };
        _demoTimer.Start();
    }

    private void PlayStartupAnimation(bool initialLaunch, bool showControlAfter)
    {
        if (_startupPeephole is not null || _petWindow is null || _controlWindow is null ||
            _statusBubble is null || _dshStatusBubble is null)
            return;

        _statusBubble.Hide();
        _dshStatusBubble.Hide();
        if (_petWindow.IsVisible) _petWindow.Hide();

        try
        {
            var intro = new StartupPeepholeWindow();
            _startupPeephole = intro;
            intro.Completed += (_, _) =>
            {
                _startupPeephole = null;
                ShowMainWindows(showControlAfter, playWave: initialLaunch);
            };
            intro.Show();
            intro.Activate();
        }
        catch
        {
            _startupPeephole = null;
            ShowMainWindows(showControlAfter, playWave: initialLaunch);
        }
    }

    private void ShowMainWindows(bool showControl, bool playWave)
    {
        if (_petWindow is null || _controlWindow is null || _statusBubble is null || _dshStatusBubble is null) return;
        if (!_petWindow.IsVisible) _petWindow.Show();
        if (_statusBubble.Owner is null) _statusBubble.Owner = _petWindow;
        if (_dshStatusBubble.Owner is null) _dshStatusBubble.Owner = _petWindow;
        if (_lastCodexActivity is not null) _statusBubble.UpdateActivity(_lastCodexActivity);
        if (_lastDshActivity is not null) _dshStatusBubble.UpdateActivity(_lastDshActivity);
        if (showControl) _controlWindow.ShowAndActivate();
        if (playWave) _petWindow.PlayTransient("waving", autoClear: true);
    }

    private void ScheduleBubblePreview(string requestedState)
    {
        if (_statusBubble is null || _dshStatusBubble is null) return;
        var previewBoth = requestedState.Equals("both", StringComparison.OrdinalIgnoreCase);
        var state = requestedState.ToLowerInvariant() switch
        {
            "waiting" => "waiting",
            "failed" => "failed",
            "review" => "review",
            _ => "running"
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _statusBubble.ActivityBubbleEnabled = true;
            _statusBubble.DisplaySeconds = 30;
            var statusLabel = state switch
            {
                "waiting" => LocalizationService.T("需要确认"),
                "failed" => LocalizationService.T("出错了"),
                "review" => LocalizationService.T("已完成"),
                _ => LocalizationService.T("工作中")
            };
            var message = state switch
            {
                "waiting" => LocalizationService.T("Codex 等待你的选择"),
                "failed" => LocalizationService.T("点击返回任务查看"),
                "review" => LocalizationService.T("已经准备好啦"),
                _ => LocalizationService.T("正在整理当前任务…")
            };
            var task = new CodexTaskSummary(
                "ui-preview", LocalizationService.T("咕嘎 UI 实机预览"), state, statusLabel, message, DateTimeOffset.Now);
            _statusBubble.UpdateActivity(new CodexActivityState(
                state, message, DateTimeOffset.Now, task.ThreadId, new[] { task }));
            if (!previewBoth) return;

            var dshTask = new CodexTaskSummary(
                "dsh-ui-preview",
                LocalizationService.T("DSH 任务"),
                "waiting",
                LocalizationService.T("需要输入"),
                LocalizationService.T("DSH 需要你的输入或批准"),
                DateTimeOffset.Now,
                "dsh");
            _dshStatusBubble.ActivityBubbleEnabled = true;
            _dshStatusBubble.DisplaySeconds = 30;
            _dshStatusBubble.UpdateActivity(new CodexActivityState(
                "waiting",
                dshTask.Message,
                dshTask.UpdatedAt,
                dshTask.ThreadId,
                new[] { dshTask },
                "dsh"));
        };
        timer.Start();
    }

    private void ApplyPetSettings(AppSettings value)
    {
        if (_petWindow is null) return;
        _petWindow.SetPetWidth(value.PetWidth);
        _petWindow.Opacity = Math.Clamp(value.Opacity, 0.2, 1);
        _petWindow.AnimationSpeed = value.AnimationSpeed;
        _petWindow.GazeEnabled = value.GazeEnabled;
        _petWindow.GazeHoldSeconds = value.GazeHoldSeconds;
        _petWindow.StayOnTop = value.StayOnTop;
        _petWindow.ReducedMotion = value.ReducedMotion;
        _petWindow.AutoIdleActions = value.AutoIdleActions;
        _petWindow.IdleActionIntervalSeconds = value.IdleActionIntervalSeconds;
        _petWindow.AutoRoam = value.AutoRoam;
        _petWindow.RoamSpeed = value.RoamSpeed;
        _petWindow.ChaseFastCursor = value.ChaseFastCursor;
        _petWindow.EdgeActionsEnabled = value.EdgeActionsEnabled;
        if (value.Left is double left && value.Top is double top)
            _petWindow.PlaceInsideWorkArea(left, top);
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            StartupManager.SetEnabled(enabled);
            _settings.StartWithWindows = enabled;
        }
        catch
        {
            _settings.StartWithWindows = StartupManager.IsEnabled();
        }
        _controlWindow?.SetStartup(_settings.StartWithWindows);
        _tray?.SetStartup(_settings.StartWithWindows);
        QueueSave();
    }

    private void SetCodexStartup(bool enabled)
    {
        try
        {
            CodexLaunchWatcherManager.SetEnabled(enabled);
            _settings.StartWithCodex = enabled;
        }
        catch
        {
            try { _settings.StartWithCodex = CodexLaunchWatcherManager.IsEnabled(); }
            catch { _settings.StartWithCodex = false; }
        }
        _controlWindow?.SetCodexStartup(_settings.StartWithCodex);
        QueueSave();
    }

    private void SynchronizeCodexStartupIntegration()
    {
        try { _settings.StartWithCodex = CodexLaunchWatcherManager.IsEnabled(); }
        catch { _settings.StartWithCodex = false; }
    }

    private void PrepareFilesForCodex(IReadOnlyList<string> files)
    {
        if (_statusBubble is null) return;
        var existing = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (existing.Length == 0)
        {
            _statusBubble.ShowNotice(
                LocalizationService.T("文件未加入"),
                LocalizationService.T("拖入的项目不是可读取文件。"));
            return;
        }

        try
        {
            var paths = new StringCollection();
            paths.AddRange(existing);
            System.Windows.Clipboard.SetFileDropList(paths);
        }
        catch
        {
            try { System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, existing)); }
            catch { }
        }

        CodexWindowActivator.ActivateOrLaunch();
        _statusBubble.ShowNotice(
            LocalizationService.T("文件已准备"),
            existing.Length == 1
                ? LocalizationService.F("已复制 {0}，在 Codex 输入框粘贴即可加入任务。", Path.GetFileName(existing[0]))
                : LocalizationService.F("已复制 {0} 个文件，在 Codex 输入框粘贴即可加入任务。", existing.Length));
    }

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSettings()
    {
        if (_demoCapture) return;
        if (_petWindow is null || _controlWindow is null || _statusBubble is null) return;
        _settings.Left = _petWindow.Left;
        _settings.Top = _petWindow.Top;
        _settings.PetWidth = _petWindow.Width;
        _settings.Opacity = _petWindow.Opacity;
        _settings.AnimationSpeed = _petWindow.AnimationSpeed;
        _settings.GazeEnabled = _petWindow.GazeEnabled;
        _settings.GazeHoldSeconds = _petWindow.GazeHoldSeconds;
        _settings.StayOnTop = _petWindow.StayOnTop;
        _settings.ReducedMotion = _petWindow.ReducedMotion;
        _settings.AutoIdleActions = _petWindow.AutoIdleActions;
        _settings.IdleActionIntervalSeconds = _petWindow.IdleActionIntervalSeconds;
        _settings.AutoRoam = _petWindow.AutoRoam;
        _settings.RoamSpeed = _petWindow.RoamSpeed;
        _settings.ChaseFastCursor = _petWindow.ChaseFastCursor;
        _settings.EdgeActionsEnabled = _petWindow.EdgeActionsEnabled;
        _settings.CodexSyncEnabled = _controlWindow.CodexSyncEnabled;
        _settings.ActivityBubbleEnabled = _statusBubble.ActivityBubbleEnabled;
        _settings.BubbleDisplaySeconds = _statusBubble.DisplaySeconds;
        _settings.StartupAnimationEnabled = _controlWindow.StartupAnimationEnabled;
        _settings.ShowControlPanelOnLaunch = _controlWindow.ShowControlPanelOnLaunch;
        _settings.Language = _controlWindow.SelectedLanguage;
        try { SettingsStore.Save(_settings); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ExitApplication()
    {
        _demoTimer?.Stop();
        _saveTimer.Stop();
        SaveSettings();
        _bridge?.Dispose();
        _codexActivity?.Dispose();
        _dshActivity?.Dispose();
        _tray?.Dispose();
        _controlWindow?.ForceClose();
        _statusBubble?.Close();
        _dshStatusBubble?.Close();
        _petWindow?.Close();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        Shutdown();
    }
}
