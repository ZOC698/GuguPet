using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GuguPet;

public partial class ControlWindow : Window
{
    private readonly PetWindow _pet;
    private readonly StatusBubbleWindow _bubble;
    private bool _forceClose;
    private bool _loading;
    private System.Windows.Point _cookieDragStart;

    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsChanged;
    public event EventHandler<bool>? StartupChanged;
    public event EventHandler<bool>? CodexStartupChanged;
    public event EventHandler? PreviewStartupRequested;
    public bool CodexSyncEnabled => CodexSyncCheck.IsChecked == true;
    public bool StartupAnimationEnabled => StartupAnimationCheck.IsChecked == true;
    public bool ShowControlPanelOnLaunch => ShowControlOnLaunchCheck.IsChecked == true;
    public string SelectedLanguage => LanguageCombo.SelectedValue as string ?? "auto";

    public ControlWindow(PetWindow pet, StatusBubbleWindow bubble, AppSettings settings)
    {
        _pet = pet;
        _bubble = bubble;
        _loading = true;
        InitializeComponent();
        LocalizationService.Apply(this);
        LanguageCombo.ItemsSource = new[]
            {
                new LanguageOption("auto", LocalizationService.T("自动（跟随 Windows）"))
            }
            .Concat(LocalizationService.InstalledLanguages)
            .ToArray();
        LanguageCombo.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        LanguageCombo.SelectedValuePath = nameof(LanguageOption.Code);
        LanguageCombo.SelectedValue = settings.Language;
        if (LanguageCombo.SelectedIndex < 0) LanguageCombo.SelectedValue = "auto";
        StateCombo.ItemsSource = AnimationCatalog.StateNames
            .Select(state => new LanguageOption(state, StateDisplayName(state)))
            .ToArray();
        StateCombo.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        StateCombo.SelectedValuePath = nameof(LanguageOption.Code);
        StateCombo.SelectedValue = "idle";
        SizeSlider.Value = settings.PetWidth;
        SpeedSlider.Value = settings.AnimationSpeed;
        OpacitySlider.Value = settings.Opacity;
        GazeCheck.IsChecked = settings.GazeEnabled;
        GazeHoldSlider.Value = settings.GazeHoldSeconds;
        TopmostCheck.IsChecked = settings.StayOnTop;
        ReducedMotionCheck.IsChecked = settings.ReducedMotion;
        AutoIdleActionsCheck.IsChecked = settings.AutoIdleActions;
        IdleIntervalSlider.Value = settings.IdleActionIntervalSeconds;
        AutoRoamCheck.IsChecked = settings.AutoRoam;
        RoamSpeedSlider.Value = settings.RoamSpeed;
        ChaseCursorCheck.IsChecked = settings.ChaseFastCursor;
        EdgeActionsCheck.IsChecked = settings.EdgeActionsEnabled;
        CodexSyncCheck.IsChecked = settings.CodexSyncEnabled;
        ActivityBubbleCheck.IsChecked = settings.ActivityBubbleEnabled;
        BubbleDurationSlider.Value = settings.BubbleDisplaySeconds;
        StartupCheck.IsChecked = settings.StartWithWindows;
        SetCodexStartup(settings.StartWithCodex);
        StartupAnimationCheck.IsChecked = settings.StartupAnimationEnabled;
        ShowControlOnLaunchCheck.IsChecked = settings.ShowControlPanelOnLaunch;
        _loading = false;
        Closing += (_, e) =>
        {
            if (_forceClose) return;
            e.Cancel = true;
            Hide();
        };
    }

    public void SetAutoRoam(bool enabled) => AutoRoamCheck.IsChecked = enabled;

    public void SetStartup(bool enabled)
    {
        _loading = true;
        StartupCheck.IsChecked = enabled;
        _loading = false;
    }

    public void SetCodexStartup(bool enabled)
    {
        _loading = true;
        CodexStartupCheck.IsChecked = enabled;
        CodexStartupHint.Text = enabled
            ? LocalizationService.T("已启用 Windows 系统事件监听；空闲时不轮询，打开 Codex 即可唤醒咕嘎。")
            : LocalizationService.T("启用后注册轻量事件监听器，不写入 Codex Hook；默认关闭。");
        _loading = false;
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public void UpdateBridgeStatus(BridgeState state)
    {
        BridgeStatus.Text = $"{StateDisplayName(state.State)} · {state.Message}\n{state.UpdatedAt:HH:mm:ss}";
        StateCombo.SelectedValue = state.State;
    }

    public void UpdateCodexStatus(CodexActivityState state)
    {
        CodexStatus.Text = $"{StateDisplayName(state.State)} · {state.Message}\n{state.UpdatedAt:HH:mm:ss}";
        HeaderStatusText.Text = state.State switch
        {
            "running" => LocalizationService.T("Codex 正在工作"),
            "waiting" => LocalizationService.T("等待你的确认"),
            "failed" => LocalizationService.T("任务遇到问题"),
            "review" => LocalizationService.T("任务已完成"),
            _ => LocalizationService.T("待机中")
        };
        HeaderStatusDot.Fill = BrushFromHex(state.State switch
        {
            "running" => "#26AFC2",
            "waiting" => "#F5A623",
            "failed" => "#E76554",
            "review" => "#3BA58A",
            _ => "#A9AAA7"
        });
        TaskList.ItemsSource = state.Tasks;
        if (CodexSyncEnabled)
            StateCombo.SelectedValue = state.State;
    }

    private void StateCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StateCombo.SelectedValue is string state)
            _pet.SetBaseState(state);
    }

    private void Wave_OnClick(object sender, RoutedEventArgs e) => _pet.PlayTransient("waving", autoClear: true);
    private void Jump_OnClick(object sender, RoutedEventArgs e) => _pet.PlayTransient("jumping", autoClear: true);
    private void Idle_OnClick(object sender, RoutedEventArgs e)
    {
        _pet.ClearTransient();
        _pet.SetBaseState("idle");
        StateCombo.SelectedValue = "idle";
    }

    private void IdleAction_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string state })
            _pet.PlayTransient(state, autoClear: true);
    }

    private void AutoIdleActionsCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && !_loading)
            _pet.AutoIdleActions = AutoIdleActionsCheck.IsChecked == true;
        NotifySettingsChanged();
    }

    private void IdleIntervalSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        _pet.IdleActionIntervalSeconds = e.NewValue;
        IdleIntervalValue.Text = LocalizationService.F("{0:0} 秒", e.NewValue);
        NotifySettingsChanged();
    }

    private void AutoRoamCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && !_loading)
            _pet.AutoRoam = AutoRoamCheck.IsChecked == true;
        NotifySettingsChanged();
    }

    private void RoamSpeedSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        _pet.RoamSpeed = e.NewValue;
        RoamSpeedValue.Text = $"{e.NewValue:0} px/s";
        NotifySettingsChanged();
    }

    private void ChaseCursorCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && !_loading)
            _pet.ChaseFastCursor = ChaseCursorCheck.IsChecked == true;
        NotifySettingsChanged();
    }

    private void EdgeActionsCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && !_loading)
            _pet.EdgeActionsEnabled = EdgeActionsCheck.IsChecked == true;
        NotifySettingsChanged();
    }

    private void RoamNow_OnClick(object sender, RoutedEventArgs e)
    {
        StateCombo.SelectedValue = "idle";
        _pet.RoamNow();
    }

    private void EdgeActionNow_OnClick(object sender, RoutedEventArgs e)
    {
        StateCombo.SelectedValue = "idle";
        _pet.EdgeActionNow();
    }

    private void SizeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        _pet.SetPetWidth(e.NewValue);
        SizeValue.Text = $"{e.NewValue:0} px";
        NotifySettingsChanged();
    }

    private void SpeedSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        _pet.AnimationSpeed = e.NewValue;
        SpeedValue.Text = $"{e.NewValue:0.00}×";
        NotifySettingsChanged();
    }

    private void OpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        _pet.Opacity = e.NewValue;
        OpacityValue.Text = $"{e.NewValue:P0}";
        NotifySettingsChanged();
    }

    private void GazeCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) _pet.GazeEnabled = GazeCheck.IsChecked == true;
        NotifySettingsChanged();
    }

    private void ActivityBubbleCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
            _bubble.ActivityBubbleEnabled = ActivityBubbleCheck.IsChecked == true;
        NotifySettingsChanged();
    }

    private void BubbleDurationSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        _bubble.DisplaySeconds = e.NewValue;
        BubbleDurationValue.Text = LocalizationService.F("{0:0} 秒", e.NewValue);
        NotifySettingsChanged();
    }

    private void GazeHoldSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        _pet.GazeHoldSeconds = e.NewValue;
        GazeHoldValue.Text = LocalizationService.F("{0:0.0} 秒", e.NewValue);
        NotifySettingsChanged();
    }

    private void TopmostCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) _pet.StayOnTop = TopmostCheck.IsChecked == true;
        NotifySettingsChanged();
    }

    private void ReducedMotionCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) _pet.ReducedMotion = ReducedMotionCheck.IsChecked == true;
        NotifySettingsChanged();
    }

    private void StartupCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && !_loading)
            StartupChanged?.Invoke(this, StartupCheck.IsChecked == true);
    }

    private void CodexStartupCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && !_loading)
            CodexStartupChanged?.Invoke(this, CodexStartupCheck.IsChecked == true);
    }

    private void LanguageCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _loading) return;
        LanguageRestartHint.Text = LocalizationService.T("更改语言后重启咕嘎生效。");
        NotifySettingsChanged();
    }

    private void StartupAnimationCheck_OnChanged(object sender, RoutedEventArgs e) => NotifySettingsChanged();

    private void ShowControlOnLaunchCheck_OnChanged(object sender, RoutedEventArgs e) => NotifySettingsChanged();

    private void PreviewStartup_OnClick(object sender, RoutedEventArgs e) =>
        PreviewStartupRequested?.Invoke(this, EventArgs.Empty);

    private void FeedCookie_OnClick(object sender, RoutedEventArgs e) =>
        _pet.PlayTransient("cookie", autoClear: true);

    private void CookieDragButton_OnMouseDown(object sender, MouseButtonEventArgs e) =>
        _cookieDragStart = e.GetPosition(this);

    private void CookieDragButton_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _cookieDragStart.X) < 5 && Math.Abs(point.Y - _cookieDragStart.Y) < 5) return;
        var data = new System.Windows.DataObject();
        data.SetData("GuguPet.Cookie", true);
        System.Windows.DragDrop.DoDragDrop(CookieDragButton, data, System.Windows.DragDropEffects.Copy);
    }

    private void TaskList_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TaskList.SelectedItem is CodexTaskSummary)
            CodexWindowActivator.ActivateOrLaunch();
    }

    private void NewCodexTask_OnClick(object sender, RoutedEventArgs e) =>
        CodexWindowActivator.ActivateOrLaunch();

    private void CodexSyncCheck_OnChanged(object sender, RoutedEventArgs e) => NotifySettingsChanged();

    private void NotifySettingsChanged()
    {
        if (IsInitialized && !_loading)
            SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDirectory) { UseShellExecute = true });
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        DragMove();
        e.Handled = true;
    }

    private void MinimizeControl_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void HideControl_OnClick(object sender, RoutedEventArgs e) => Hide();

    private static SolidColorBrush BrushFromHex(string hex) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private static string StateDisplayName(string state) => state switch
    {
        "idle" => LocalizationService.T("待机"),
        "running-right" => LocalizationService.T("向右移动"),
        "running-left" => LocalizationService.T("向左移动"),
        "waving" => LocalizationService.T("挥手"),
        "jumping" => LocalizationService.T("跳跃"),
        "failed" => LocalizationService.T("失败"),
        "waiting" => LocalizationService.T("等待输入"),
        "running" => LocalizationService.T("思考中"),
        "review" => LocalizationService.T("完成审阅"),
        _ => state
    };

    private void Exit_OnClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
