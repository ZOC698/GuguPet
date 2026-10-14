using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GuguPet;

public partial class StatusBubbleWindow : Window
{
    private readonly PetWindow _pet;
    private readonly bool _preferRight;
    private readonly string _source;
    private IReadOnlyList<StatusBubbleWindow>? _peers;
    private bool _layoutPending;
    private readonly DispatcherTimer _hideTimer;
    private bool _activityBubbleEnabled = true;
    private double _displaySeconds = 10;
    private string? _lastSignature;
    private CodexActivityState? _activity;
    private int _taskIndex;

    public event Action<string>? ActivityRequested;

    public StatusBubbleWindow(PetWindow pet, bool preferRight = true, string source = "codex")
    {
        _pet = pet;
        _preferRight = preferRight;
        _source = source;
        InitializeComponent();
        LocalizationService.Apply(this);
        Title = $"GuguPet {ActivitySources.Label(source)} Progress";
        var isDsh = source.Equals("dsh", StringComparison.OrdinalIgnoreCase);
        var isAntigravity = source.Equals("antigravity", StringComparison.OrdinalIgnoreCase);
        var dshWaveVisibility = isDsh ? Visibility.Visible : Visibility.Collapsed;
        DshWaveTop.Visibility = dshWaveVisibility;
        DshWaveBottom.Visibility = dshWaveVisibility;
        DshWaveRight.Visibility = dshWaveVisibility;
        var codexBorderVisibility = !isDsh && !isAntigravity ? Visibility.Visible : Visibility.Collapsed;
        CodexBorderTop.Visibility = codexBorderVisibility;
        CodexBorderBottom.Visibility = codexBorderVisibility;
        CodexBorderRight.Visibility = codexBorderVisibility;
        CodexSourceIcon.Visibility = !isDsh && !isAntigravity ? Visibility.Visible : Visibility.Collapsed;
        DshSourceIcon.Visibility = isDsh ? Visibility.Visible : Visibility.Collapsed;
        AntigravitySourceIcon.Visibility = isAntigravity ? Visibility.Visible : Visibility.Collapsed;
        _pet.LocationChanged += (_, _) => FollowPet();
        _pet.SizeChanged += (_, _) => FollowPet();
        SizeChanged += (_, _) => ScheduleLayout();
        IsVisibleChanged += (_, _) => ScheduleLayout();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_displaySeconds) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    public void SetPeers(IReadOnlyList<StatusBubbleWindow> peers) => _peers = peers;

    private void ScheduleLayout()
    {
        if (_layoutPending) return;
        _layoutPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _layoutPending = false;
            FollowPet();
        }));
    }

    public bool ActivityBubbleEnabled
    {
        get => _activityBubbleEnabled;
        set
        {
            if (_activityBubbleEnabled == value) return;
            _activityBubbleEnabled = value;
            _lastSignature = null;
            if (!value) Hide();
            else if (_activity is not null) UpdateActivity(_activity);
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
            _activity = activity;
            _lastSignature = null;
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

        var sourceLabel = task?.SourceLabel ?? _activity.SourceLabel;
        StateText.Text = state switch
        {
            "running" => LocalizationService.F("{0} 正在工作", sourceLabel),
            "waiting" => LocalizationService.T("需要你确认"),
            "failed" => LocalizationService.T("任务遇到问题"),
            "review" => LocalizationService.T("任务完成"),
            "interrupted" => LocalizationService.T("任务已中止"),
            _ => LocalizationService.F("{0} 状态", sourceLabel)
        };
        ApplyStateTheme(state);
        TaskTitleText.Text = task?.Title ?? "";
        TaskTitleText.Visibility = task is null ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = string.IsNullOrWhiteSpace(task?.Message) ? _activity.Message : task.Message;
        TaskNavigation.Visibility = _activity.Tasks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        TaskCounterText.Text = _activity.Tasks.Count > 0 ? $"{_taskIndex + 1}/{_activity.Tasks.Count}" : "";
        ActionHintText.Text = LocalizationService.F("点击气泡返回 {0}", sourceLabel);
        ActionButton.Visibility = state is "waiting" or "failed" ? Visibility.Visible : Visibility.Collapsed;
        ActionButton.Content = LocalizationService.F("返回 {0}", sourceLabel);
    }

    private void ApplyStateTheme(string state)
    {
        var accent = BrushFromHex(state switch
        {
            "running" => "#26AFC2",
            "waiting" => "#F5A623",
            "failed" => "#E76554",
            "review" => "#3BA58A",
            "interrupted" => "#8C7A6B",
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
        if (_peers is not null)
        {
            ArrangePeers();
            return;
        }
        var area = _pet.CurrentDisplayWorkArea;
        var right = _pet.Left + _pet.ActualWidth + 10;
        var left = _pet.Left - ActualWidth - 10;
        var preferredFits = _preferRight
            ? right + ActualWidth <= area.Right
            : left >= area.Left;
        Left = _preferRight
            ? (preferredFits ? right : Math.Max(area.Left, left))
            : (preferredFits ? left : Math.Min(area.Right - ActualWidth, right));

        var baseTop = _pet.Top + 18;
        var desiredTop = preferredFits ? baseTop : baseTop + ActualHeight + 8;
        if (!preferredFits && desiredTop + ActualHeight > area.Bottom)
            desiredTop = baseTop - ActualHeight - 8;
        Top = Math.Clamp(desiredTop, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }

    private void ArrangePeers()
    {
        var area = _pet.CurrentDisplayWorkArea;
        var pet = new Rect(_pet.Left, _pet.Top, _pet.ActualWidth, _pet.ActualHeight);
        var placed = new List<Rect>();
        foreach (var bubble in _peers!.Where(peer => peer.IsVisible))
        {
            var width = bubble.ActualWidth > 0 ? bubble.ActualWidth : bubble.Width;
            var height = bubble.ActualHeight > 0 ? bubble.ActualHeight : 180;
            var right = pet.Right + 10;
            var left = pet.Left - width - 10;
            var xs = bubble._preferRight ? new[] { right, left } : new[] { left, right };
            var ys = new[] { pet.Top + 18 }
                .Concat(placed.Select(rect => rect.Bottom + 8))
                .Concat(placed.Select(rect => rect.Top - height - 8))
                .Concat(new[] { pet.Top - height - 8, area.Top, area.Bottom - height });
            var candidates = xs.SelectMany((x, side) => ys.Select(y =>
            {
                var rect = new Rect(
                    Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)),
                    Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height)), width, height);
                var overlap = placed.Sum(other => Overlap(rect, other));
                var score = overlap * 1000 + Overlap(rect, pet) * 1000 + side * 100 + Math.Abs(rect.Top - pet.Top - 18);
                return (rect, score);
            }));
            var best = candidates.OrderBy(candidate => candidate.score).First().rect;
            bubble.Left = best.Left;
            bubble.Top = best.Top;
            placed.Add(best);
        }
    }

    private static double Overlap(Rect first, Rect second)
    {
        first.Intersect(second);
        return first.IsEmpty ? 0 : first.Width * first.Height;
    }

    private void Bubble_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ActivityRequested?.Invoke(SelectedSource());
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
        ActivityRequested?.Invoke(SelectedSource());
        e.Handled = true;
    }

    private string SelectedSource()
    {
        if (_activity is null) return _source;
        return _activity.Tasks.Count > 0
            ? _activity.Tasks[Math.Clamp(_taskIndex, 0, _activity.Tasks.Count - 1)].Source
            : _activity.Source;
    }
}
