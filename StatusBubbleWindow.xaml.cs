using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GuguPet;

public partial class StatusBubbleWindow : Window
{
    private readonly PetWindow _pet;
    private readonly DispatcherTimer _hideTimer;
    private bool _activityBubbleEnabled = true;
    private double _displaySeconds = 10;
    private string? _lastSignature;
    private CodexActivityState? _activity;
    private int _taskIndex;

    public event EventHandler? ActivityRequested;

    public StatusBubbleWindow(PetWindow pet)
    {
        _pet = pet;
        InitializeComponent();
        LocalizationService.Apply(this);
        _pet.LocationChanged += (_, _) => FollowPet();
        _pet.SizeChanged += (_, _) => FollowPet();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_displaySeconds) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    public bool ActivityBubbleEnabled
    {
        get => _activityBubbleEnabled;
        set
        {
            _activityBubbleEnabled = value;
            if (!value) Hide();
        }
    }

    public double DisplaySeconds
    {
        get => _displaySeconds;
        set => _displaySeconds = Math.Clamp(value, 3, 30);
    }

    public void UpdateActivity(CodexActivityState activity)
    {
        if (!_activityBubbleEnabled || activity.State == "idle")
        {
            if (activity.State == "idle") Hide();
            return;
        }

        var signature = $"{activity.State}|{activity.Message}|{activity.UpdatedAt:O}|" +
                        string.Join(';', activity.Tasks.Select(task =>
                            $"{task.ThreadId}:{task.State}:{task.UpdatedAt:O}:{task.Message}"));
        if (signature == _lastSignature) return;
        _lastSignature = signature;
        _activity = activity;
        _taskIndex = 0;
        RenderSelectedTask();

        FollowPet();
        if (!IsVisible) Show();
        FollowPet();
        RestartHideTimer();
    }

    public void ShowNotice(string title, string message)
    {
        _activity = null;
        _taskIndex = 0;
        StateText.Text = title;
        ApplyStateTheme("running");
        TaskTitleText.Visibility = Visibility.Collapsed;
        MessageText.Text = message;
        TaskNavigation.Visibility = Visibility.Collapsed;
        ActionButton.Visibility = Visibility.Collapsed;
        FollowPet();
        if (!IsVisible) Show();
        FollowPet();
        RestartHideTimer();
    }

    private void RenderSelectedTask()
    {
        if (_activity is null) return;
        var task = _activity.Tasks.Count > 0
            ? _activity.Tasks[Math.Clamp(_taskIndex, 0, _activity.Tasks.Count - 1)]
            : null;
        var state = task?.State ?? _activity.State;

        StateText.Text = state switch
        {
            "running" => LocalizationService.T("Codex 正在工作"),
            "waiting" => LocalizationService.T("需要你确认"),
            "failed" => LocalizationService.T("任务遇到问题"),
            "review" => LocalizationService.T("任务完成"),
            _ => LocalizationService.T("Codex 状态")
        };
        ApplyStateTheme(state);
        TaskTitleText.Text = task?.Title ?? "";
        TaskTitleText.Visibility = task is null ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = string.IsNullOrWhiteSpace(task?.Message) ? _activity.Message : task.Message;
        TaskNavigation.Visibility = _activity.Tasks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        TaskCounterText.Text = _activity.Tasks.Count > 0 ? $"{_taskIndex + 1}/{_activity.Tasks.Count}" : "";
        ActionButton.Visibility = state is "waiting" or "failed" ? Visibility.Visible : Visibility.Collapsed;
        ActionButton.Content = LocalizationService.T("返回 Codex");
    }

    private void ApplyStateTheme(string state)
    {
        var accent = BrushFromHex(state switch
        {
            "running" => "#26AFC2",
            "waiting" => "#F5A623",
            "failed" => "#E76554",
            "review" => "#3BA58A",
            _ => "#888888"
        });
        StateDot.Fill = accent;
        BubbleAccentBar.Background = accent;
        ActionButton.Background = accent;
        ActionButton.BorderBrush = accent;
    }

    private static SolidColorBrush BrushFromHex(string hex) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private void RestartHideTimer()
    {
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromSeconds(_displaySeconds);
        _hideTimer.Start();
    }

    private void FollowPet()
    {
        if (!IsVisible && !IsLoaded) return;
        var area = _pet.CurrentDisplayWorkArea;
        var preferredLeft = _pet.Left + _pet.ActualWidth + 10;
        Left = preferredLeft + ActualWidth <= area.Right
            ? preferredLeft
            : Math.Max(area.Left, _pet.Left - ActualWidth - 10);
        Top = Math.Clamp(_pet.Top + 18, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }

    private void Bubble_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ActivityRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void PreviousTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activity is null || _activity.Tasks.Count == 0) return;
        _taskIndex = (_taskIndex - 1 + _activity.Tasks.Count) % _activity.Tasks.Count;
        RenderSelectedTask();
        RestartHideTimer();
        e.Handled = true;
    }

    private void NextTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activity is null || _activity.Tasks.Count == 0) return;
        _taskIndex = (_taskIndex + 1) % _activity.Tasks.Count;
        RenderSelectedTask();
        RestartHideTimer();
        e.Handled = true;
    }

    private void ActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        ActivityRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
