using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GuguPet;

public partial class PetWindow : Window
{
    private enum EdgeActionStage { None, Approach, Peek, Rest, Return }

    private readonly BitmapImage _sheet;
    private readonly BitmapImage _idleActionSheet;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private AnimationSequence _sequence = AnimationCatalog.GetSequence("idle", false);
    private int _frameIndex;
    private string _baseState = "idle";
    private string? _transientState;
    private string? _thinkingState;
    private readonly Stopwatch _thinkingClock = Stopwatch.StartNew();
    private readonly Stopwatch _progressGestureClock = Stopwatch.StartNew();
    private readonly Stopwatch _attentionClock = new();
    private System.Windows.Point _attentionScreenPoint;
    private bool _attentionActive;
    private bool _attentionPending;
    private bool _hasShownProgressGesture;
    private double _attentionSeconds = 1.4;
    private readonly Stopwatch _runningDurationClock = new();
    private double _nextRunningBreakSeconds = 120;
    private readonly Stopwatch _inactivityClock = Stopwatch.StartNew();
    private double _nextThinkingChangeSeconds = 8;
    private bool _pointerDown;
    private bool _dragging;
    private bool _resizing;
    private POINT _dragStartCursor;
    private POINT _lastDragCursor;
    private readonly Stopwatch _dragMotionClock = new();
    private double _dragVelocityX;
    private double _dragVelocityY;
    private bool _inertial;
    private readonly Stopwatch _inertiaStepClock = new();
    private double _dragStartLeft;
    private double _dragStartTop;
    private System.Windows.Point _pointerDownLocal;
    private int _pointerClickCount;
    private double _resizeStartWidth;
    private double _resizeStartCursorX;
    private bool _reducedMotion;
    private bool _gazeEnabled = true;
    private readonly Stopwatch _gazeHoldClock = new();
    private bool _hasGazeCursorSample;
    private POINT _lastGazeCursor;
    private double _gazeHoldSeconds = 1.5;
    private double _animationSpeed = 1;
    private bool _autoClearTransient;
    private readonly Stopwatch _idleActionClock = Stopwatch.StartNew();
    private readonly Random _random = new();
    private bool _autoIdleActions = true;
    private double _idleActionIntervalSeconds = 45;
    private readonly Stopwatch _roamClock = Stopwatch.StartNew();
    private readonly Stopwatch _roamStepClock = new();
    private bool _autoRoam = true;
    private bool _roaming;
    private double _roamTargetLeft;
    private double _roamTargetTop;
    private double _roamSpeed = 72;
    private double _nextRoamDelaySeconds;
    private bool _chaseFastCursor;
    private readonly Stopwatch _cursorSpeedClock = Stopwatch.StartNew();
    private readonly Stopwatch _chaseCooldownClock = Stopwatch.StartNew();
    private bool _hasCursorSpeedSample;
    private POINT _lastSpeedCursor;
    private bool _edgeActionsEnabled = true;
    private EdgeActionStage _edgeActionStage;
    private DisplayEdge _edgeActionEdge;
    private Rect _edgeWorkArea;
    private readonly Stopwatch _edgeActionClock = Stopwatch.StartNew();
    private double _nextEdgeActionSeconds;

    public event EventHandler? OpenControlsRequested;
    public event EventHandler? NewCodexTaskRequested;
    public event Action<CodexTaskSummary>? RecentCodexTaskRequested;
    public event Action<IReadOnlyList<string>>? FilesDropped;
    public event EventHandler? SettingsChanged;
    public PetWindow()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        _sheet = new BitmapImage();
        _sheet.BeginInit();
        _sheet.CacheOption = BitmapCacheOption.OnLoad;
        _sheet.UriSource = new Uri("pack://application:,,,/Assets/spritesheet.png", UriKind.Absolute);
        _sheet.EndInit();
        _sheet.Freeze();

        _idleActionSheet = new BitmapImage();
        _idleActionSheet.BeginInit();
        _idleActionSheet.CacheOption = BitmapCacheOption.OnLoad;
        _idleActionSheet.UriSource = new Uri("pack://application:,,,/Assets/idle-actions.png", UriKind.Absolute);
        _idleActionSheet.EndInit();
        _idleActionSheet.Freeze();

        Left = SystemParameters.WorkArea.Right - Width - 40;
        Top = SystemParameters.WorkArea.Bottom - Height - 40;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        ScheduleNextRoam();
        ScheduleNextEdgeAction();

        MouseEnter += (_, _) =>
        {
            CancelEdgeAction();
            StopRoaming();
            if (!_dragging && !_resizing && _baseState.Equals("idle", StringComparison.OrdinalIgnoreCase))
                PlayTransient("jumping");
            ResizeGrip.Visibility = Visibility.Visible;
        };
        MouseLeave += (_, _) =>
        {
            ResizeGrip.Visibility = Visibility.Collapsed;
            if (!_dragging && !_resizing)
                ClearTransient();
        };
        MouseLeftButtonDown += PetWindow_OnMouseLeftButtonDown;
        MouseMove += PetWindow_OnMouseMove;
        MouseLeftButtonUp += PetWindow_OnMouseLeftButtonUp;
        LocationChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        SizeChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        SourceInitialized += (_, _) => ConstrainToCurrentDisplay();
    }

    public string CurrentState => _transientState ?? _thinkingState ?? _baseState;
    public bool GazeEnabled
    {
        get => _gazeEnabled;
        set
        {
            _gazeEnabled = value;
            ResetGazeTracking();
        }
    }
    public double GazeHoldSeconds
    {
        get => _gazeHoldSeconds;
        set => _gazeHoldSeconds = Math.Clamp(value, 0.4, 4);
    }
    public bool ReducedMotion { get => _reducedMotion; set { _reducedMotion = value; RestartAnimation(); } }
    public double AnimationSpeed { get => _animationSpeed; set => _animationSpeed = Math.Clamp(value, 0.25, 3); }
    public bool StayOnTop { get => Topmost; set => Topmost = value; }
    public bool AutoIdleActions
    {
        get => _autoIdleActions;
        set
        {
            _autoIdleActions = value;
            _idleActionClock.Restart();
        }
    }
    public double IdleActionIntervalSeconds
    {
        get => _idleActionIntervalSeconds;
        set
        {
            _idleActionIntervalSeconds = Math.Clamp(value, 15, 300);
            _idleActionClock.Restart();
        }
    }
    public bool AutoRoam
    {
        get => _autoRoam;
        set
        {
            _autoRoam = value;
            if (!value) StopRoaming();
            ScheduleNextRoam();
        }
    }
    public double RoamSpeed
    {
        get => _roamSpeed;
        set => _roamSpeed = Math.Clamp(value, 30, 180);
    }
    public bool ChaseFastCursor
    {
        get => _chaseFastCursor;
        set
        {
            _chaseFastCursor = value;
            _hasCursorSpeedSample = false;
            _cursorSpeedClock.Restart();
        }
    }
    public bool EdgeActionsEnabled
    {
        get => _edgeActionsEnabled;
        set
        {
            _edgeActionsEnabled = value;
            if (!value) CancelEdgeAction();
            ScheduleNextEdgeAction();
        }
    }

    public void RoamNow()
    {
        CancelEdgeAction();
        if (_dragging || _resizing || !_baseState.Equals("idle", StringComparison.OrdinalIgnoreCase))
            return;
        if (_transientState is not null)
            ClearTransient();
        StartRoaming();
    }

    public void EdgeActionNow()
    {
        if (_dragging || _resizing || !_baseState.Equals("idle", StringComparison.OrdinalIgnoreCase))
            return;
        CancelEdgeAction(restartAnimation: false);
        if (_transientState is not null)
        {
            _transientState = null;
            _autoClearTransient = false;
        }
        StopInertia(restartAnimation: false);
        StartEdgeAction();
    }

    public void SetBaseState(string state)
    {
        // Custom idle actions are transient previews; Codex/bridge states remain
        // limited to the original v2 state machine.
        if (!AnimationCatalog.IsValidState(state) || AnimationCatalog.IsIdleAction(state)) return;
        var interruptedEdgeAction = _edgeActionStage != EdgeActionStage.None;
        CancelEdgeAction(restartAnimation: false);
        if (_baseState.Equals(state, StringComparison.OrdinalIgnoreCase))
        {
            if (interruptedEdgeAction) RestartAnimation();
            return;
        }
        StopInertia(restartAnimation: false);
        _transientState = null;
        _autoClearTransient = false;
        _attentionActive = false;
        _attentionPending = false;
        _attentionClock.Reset();
        _baseState = state;
        if (_baseState.Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            ChooseThinkingState();
            _hasShownProgressGesture = false;
            _progressGestureClock.Reset();
            _runningDurationClock.Restart();
            _nextRunningBreakSeconds = 120 + _random.NextDouble() * 90;
        }
        else
        {
            _thinkingState = null;
            _runningDurationClock.Reset();
        }
        if (!_baseState.Equals("idle", StringComparison.OrdinalIgnoreCase))
        {
            StopRoaming();
            ResetGazeTracking();
            _inactivityClock.Restart();
        }
        _idleActionClock.Restart();
        ScheduleNextRoam();

        // Give the four Codex states their own personality, then settle into
        // the canonical state. All thinking variants themselves remain
        // pixel-locked; only the authored pose changes.
        if (_baseState.Equals("waiting", StringComparison.OrdinalIgnoreCase))
            PlayTransient(_random.Next(100) < 45 ? "waving" : "needs-input", autoClear: true);
        else if (_baseState.Equals("review", StringComparison.OrdinalIgnoreCase))
            PlayTransient(ChooseCompletionAction(), autoClear: true);
        else if (_baseState.Equals("failed", StringComparison.OrdinalIgnoreCase) && _random.Next(100) < 45)
            PlayTransient("thinking-spiral", autoClear: true);
        else
            RestartAnimation();
    }

    public void AcknowledgeProgress(double screenX, double screenY)
    {
        if (!_baseState.Equals("running", StringComparison.OrdinalIgnoreCase)) return;
        _attentionScreenPoint = new System.Windows.Point(screenX, screenY);
        if (_transientState is not null ||
            (_hasShownProgressGesture && _progressGestureClock.Elapsed.TotalSeconds < 6))
        {
            _attentionPending = true;
            return;
        }
        StartProgressAttention();
    }

    private void StartProgressAttention()
    {
        _attentionPending = false;
        _hasShownProgressGesture = true;
        _progressGestureClock.Restart();
        _attentionSeconds = 2.4 + _random.NextDouble() * 0.7;
        _attentionActive = true;
        _attentionClock.Restart();
    }

    public void PlayTransient(string state, bool autoClear = false)
    {
        if (!AnimationCatalog.IsValidState(state)) return;
        CancelEdgeAction(restartAnimation: false);
        _attentionActive = false;
        _attentionClock.Reset();
        if (_roaming) StopRoaming();
        if (_inertial) StopInertia(restartAnimation: false);
        ResetGazeTracking();
        if (_transientState?.Equals(state, StringComparison.OrdinalIgnoreCase) == true &&
            _autoClearTransient == autoClear) return;
        _transientState = state;
        _autoClearTransient = autoClear;
        RestartAnimation();
    }

    public void ClearTransient()
    {
        if (_transientState is null) return;
        _transientState = null;
        _autoClearTransient = false;
        _idleActionClock.Restart();
        ScheduleNextRoam();
        ResetGazeTracking();
        RestartAnimation();
    }

    public void SetPetWidth(double width)
    {
        Width = Math.Clamp(width, 80, 448);
        Height = Width * AnimationCatalog.CellHeight / AnimationCatalog.CellWidth;
        if (PresentationSource.FromVisual(this) is not null)
            Dispatcher.BeginInvoke(ConstrainToCurrentDisplay, DispatcherPriority.Loaded);
    }

    public void UpdateRecentCodexTasks(IEnumerable<CodexTaskSummary> tasks)
    {
        var recent = tasks
            .OrderByDescending(task => task.UpdatedAt)
            .Take(3)
            .ToArray();
        var slots = new[] { RecentTask1, RecentTask2, RecentTask3 };

        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            if (index >= recent.Length)
            {
                slot.Visibility = Visibility.Collapsed;
                slot.Tag = null;
                continue;
            }

            var task = recent[index];
            slot.Header = $"{index + 1}. [{task.StatusLabel}] {MenuTaskTitle(task)}";
            slot.ToolTip = $"{task.Title}\n{LocalizationService.F("更新于 {0:MM-dd HH:mm}", task.UpdatedAt)}";
            slot.Tag = task;
            slot.Visibility = Visibility.Visible;
        }

        RecentTasksSeparator.Visibility = recent.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string ShortenMenuTitle(string title)
    {
        var defaultTitle = LocalizationService.T("Codex 任务");
        var clean = string.IsNullOrWhiteSpace(title) ? defaultTitle : title.Trim();
        return clean.Length <= 28 ? clean : clean[..27] + "…";
    }

    private static string MenuTaskTitle(CodexTaskSummary task)
    {
        var defaultTitle = LocalizationService.T("Codex 任务");
        var title = task.Title.Trim();
        if ((title.Length == 0 || IsDefaultTaskTitle(title, defaultTitle)) &&
            !string.IsNullOrWhiteSpace(task.Message))
            title = task.Message.Trim();
        if (title.Length == 0 || IsDefaultTaskTitle(title, defaultTitle))
            title = LocalizationService.F("未命名对话 · {0:MM-dd HH:mm}", task.UpdatedAt);
        return ShortenMenuTitle(title);
    }

    private static bool IsDefaultTaskTitle(string title, string localizedDefault) =>
        title.Equals("Codex 任务", StringComparison.OrdinalIgnoreCase) ||
        title.Equals(localizedDefault, StringComparison.OrdinalIgnoreCase);

    public void PlaceInsideWorkArea(double left, double top)
    {
        // Preserve a saved secondary-monitor position until WPF has created the
        // native window. SourceInitialized then resolves and clamps it against
        // the monitor that actually contains (or is nearest to) the pet.
        Left = left;
        Top = top;
        if (PresentationSource.FromVisual(this) is not null)
            ConstrainToCurrentDisplay();
    }

    internal Rect CurrentDisplayWorkArea => DisplayGeometry.ForWindow(this).WorkArea;

    private void ConstrainToCurrentDisplay()
    {
        var area = DisplayGeometry.ForWindow(this).WorkArea;
        var position = DisplayGeometry.ClampRect(area, PetWidth, PetHeight, Left, Top);
        Left = position.Left;
        Top = position.Top;
    }

    private double PetWidth => ActualWidth > 0 ? ActualWidth : Width;
    private double PetHeight => ActualHeight > 0 ? ActualHeight : Height;

    private void RestartAnimation()
    {
        var persistentCodexState = _transientState is null &&
                                   !_baseState.Equals("idle", StringComparison.OrdinalIgnoreCase);
        _sequence = _roaming || persistentCodexState
            ? AnimationCatalog.GetLoopingSequence(CurrentState, _reducedMotion)
            : AnimationCatalog.GetSequence(CurrentState, _reducedMotion);
        _frameIndex = 0;
        _frameClock.Restart();
        Display(_sequence.Frames[0]);
    }

    private void Tick()
    {
        // The pet stays hidden while its own startup vignette is playing.
        // Screen-coordinate conversion is invalid until WPF attaches this
        // window to a PresentationSource, so pause pet animation meanwhile.
        if (!IsVisible || PresentationSource.FromVisual(this) is null) return;

        if (_attentionPending &&
            _baseState.Equals("running", StringComparison.OrdinalIgnoreCase) &&
            _transientState is null &&
            (!_hasShownProgressGesture || _progressGestureClock.Elapsed.TotalSeconds >= 6))
            StartProgressAttention();

        if (_attentionActive)
        {
            if (!_baseState.Equals("running", StringComparison.OrdinalIgnoreCase) ||
                _attentionClock.Elapsed.TotalSeconds > _attentionSeconds)
            {
                _attentionActive = false;
                _attentionClock.Reset();
                RestartAnimation();
            }
            else
            {
                var localAttention = PointFromScreen(_attentionScreenPoint);
                Display(AnimationCatalog.LookFrame(
                    localAttention.X,
                    localAttention.Y,
                    ActualWidth / 2,
                    ActualHeight / 2));
                return;
            }
        }

        if (_edgeActionStage is EdgeActionStage.Peek or EdgeActionStage.Rest)
            AdvanceEdgeAction();

        if (_baseState.Equals("running", StringComparison.OrdinalIgnoreCase) && _transientState is null &&
            _thinkingClock.Elapsed.TotalSeconds >= _nextThinkingChangeSeconds)
        {
            ChooseThinkingState();
            RestartAnimation();
        }

        if (_baseState.Equals("running", StringComparison.OrdinalIgnoreCase) && _transientState is null &&
            _runningDurationClock.Elapsed.TotalSeconds >= _nextRunningBreakSeconds)
        {
            var roll = _random.Next(100);
            var breakAction = roll < 45 ? "sit-think" : roll < 75 ? "drink" : "stretch";
            _nextRunningBreakSeconds += 100 + _random.NextDouble() * 120;
            PlayTransient(breakAction, autoClear: true);
            return;
        }

        if (_roaming)
            AdvanceRoaming();
        else if (_inertial)
            AdvanceInertia();

        if (_chaseFastCursor && _edgeActionStage == EdgeActionStage.None && !_roaming && !_inertial && !_dragging && !_resizing &&
            _transientState is null && _baseState.Equals("idle", StringComparison.OrdinalIgnoreCase) &&
            TryStartFastCursorChase())
            return;

        if (_edgeActionsEnabled && _edgeActionStage == EdgeActionStage.None && !_roaming && !_inertial && !_dragging && !_resizing &&
            _transientState is null && _baseState.Equals("idle", StringComparison.OrdinalIgnoreCase) &&
            _edgeActionClock.Elapsed.TotalSeconds >= _nextEdgeActionSeconds)
        {
            StartEdgeAction();
            return;
        }

        if (_autoRoam && _edgeActionStage == EdgeActionStage.None && !_roaming && !_inertial && !_dragging && !_resizing && _transientState is null &&
            _baseState.Equals("idle", StringComparison.OrdinalIgnoreCase) &&
            _roamClock.Elapsed.TotalSeconds >= _nextRoamDelaySeconds)
        {
            StartRoaming();
        }

        if (_autoIdleActions && _edgeActionStage == EdgeActionStage.None && !_roaming && !_inertial && !_dragging && !_resizing && _transientState is null &&
            _baseState.Equals("idle", StringComparison.OrdinalIgnoreCase) &&
            _idleActionClock.Elapsed.TotalSeconds >= _idleActionIntervalSeconds)
        {
            var actions = _inactivityClock.Elapsed.TotalSeconds >= 90
                ? new[] { "sleep-side", "sleep-prone", "sleep-supine" }
                : new[] { "guitar", "cookie" };
            var action = actions[_random.Next(actions.Length)];
            PlayTransient(action, autoClear: true);
            return;
        }

        if (_gazeEnabled && !_roaming && !_inertial && !_dragging && _transientState is null &&
            CurrentState.Equals("idle", StringComparison.OrdinalIgnoreCase) &&
            TryGetCursorNearFeet(out var feetCursor))
        {
            Display(AnimationCatalog.LookFrame(
                feetCursor.X,
                feetCursor.Y,
                ActualWidth / 2,
                ActualHeight / 2));
            return;
        }

        if (_gazeEnabled && !_roaming && !_inertial && !_dragging && _transientState is null &&
            CurrentState.Equals("idle", StringComparison.OrdinalIgnoreCase) &&
            TryGetActiveGazeCursor(out var cursor))
        {
            // GetCursorPos returns physical screen pixels, while WPF window geometry uses
            // device-independent units. PointFromScreen performs the per-monitor DPI
            // conversion and also avoids mixing global and local coordinate spaces.
            var localCursor = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
            var look = AnimationCatalog.LookFrame(
                localCursor.X,
                localCursor.Y,
                ActualWidth / 2,
                ActualHeight / 2);
            Display(look);
            return;
        }

        var frame = _sequence.Frames[_frameIndex];
        var duration = Math.Max(1, frame.DurationMs / _animationSpeed);
        if (_frameClock.Elapsed.TotalMilliseconds < duration)
        {
            Display(frame);
            return;
        }

        _frameClock.Restart();
        _frameIndex++;
        if (_frameIndex >= _sequence.Frames.Count)
        {
            if (_autoClearTransient)
            {
                ClearTransient();
                return;
            }
            _frameIndex = _sequence.LoopStartIndex;
        }
        Display(_sequence.Frames[_frameIndex]);
    }

    private void Display(SpriteFrame frame)
    {
        var rect = new Int32Rect(
            frame.Column * AnimationCatalog.CellWidth,
            frame.Row * AnimationCatalog.CellHeight,
            AnimationCatalog.CellWidth,
            AnimationCatalog.CellHeight);
        var source = frame.Sheet == SpriteSheetKind.IdleActions ? _idleActionSheet : _sheet;
        SpriteImage.Source = new CroppedBitmap(source, rect);
    }

    private void PetWindow_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element && element.Name == nameof(ResizeGrip)) return;
        CancelEdgeAction();
        StopRoaming();
        StopInertia(restartAnimation: false);
        if (!GetCursorPos(out _dragStartCursor)) return;
        _lastDragCursor = _dragStartCursor;
        _dragVelocityX = 0;
        _dragVelocityY = 0;
        _dragMotionClock.Restart();
        _inactivityClock.Restart();
        _pointerDown = true;
        _dragging = false;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _pointerDownLocal = e.GetPosition(this);
        _pointerClickCount = e.ClickCount;
        CaptureMouse();
        e.Handled = true;
    }

    private void PetWindow_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pointerDown || !GetCursorPos(out var cursor)) return;
        var dx = cursor.X - _dragStartCursor.X;
        var dy = cursor.Y - _dragStartCursor.Y;
        if (!_dragging && dx * dx + dy * dy < 36) return;
        _dragging = true;
        Left = _dragStartLeft + dx;
        Top = _dragStartTop + dy;
        var elapsed = _dragMotionClock.Elapsed.TotalSeconds;
        if (elapsed >= 0.008)
        {
            var instantX = (cursor.X - _lastDragCursor.X) / elapsed;
            var instantY = (cursor.Y - _lastDragCursor.Y) / elapsed;
            _dragVelocityX = _dragVelocityX * 0.55 + instantX * 0.45;
            _dragVelocityY = _dragVelocityY * 0.55 + instantY * 0.45;
            _lastDragCursor = cursor;
            _dragMotionClock.Restart();
        }
        if (dx >= 4) PlayTransient("running-right");
        else if (dx <= -4) PlayTransient("running-left");
    }

    private void PetWindow_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown) return;
        var wasDragging = _dragging;
        _pointerDown = false;
        _dragging = false;
        ReleaseMouseCapture();
        if (wasDragging)
        {
            var speed = Math.Sqrt(_dragVelocityX * _dragVelocityX + _dragVelocityY * _dragVelocityY);
            if (speed >= 80)
                StartInertia();
            else
            {
                ConstrainToCurrentDisplay();
                ClearTransient();
            }
        }
        else if (_pointerDownLocal.Y <= ActualHeight * 0.48)
        {
            PlayTransient("head-pat", autoClear: true);
        }
        else if (_pointerClickCount >= 2)
        {
            // The belly reaction is deliberately double-click-only so ordinary
            // clicks and drag starts do not keep interrupting Codex states.
            PlayTransient("belly-poke", autoClear: true);
        }
        e.Handled = true;
    }

    private void ChooseThinkingState()
    {
        var roll = _random.Next(100);
        var index = roll < 50 ? 0 : roll < 80 ? 1 : 2;
        _thinkingState = index switch
        {
            0 => "thinking-chin",
            1 => "thinking-spiral",
            _ => "thinking-star"
        };
        _nextThinkingChangeSeconds = 8 + _random.NextDouble() * 7;
        _thinkingClock.Restart();
    }

    private string ChooseCompletionAction()
    {
        var roll = _random.Next(100);
        return roll switch
        {
            < 25 => "celebrate-cheer",
            < 45 => "celebrate-clap",
            < 60 => "celebrate-dance",
            < 75 => "thinking-star",
            < 90 => "jumping",
            _ => "cookie"
        };
    }

    private void StartInertia()
    {
        var speed = Math.Sqrt(_dragVelocityX * _dragVelocityX + _dragVelocityY * _dragVelocityY);
        if (speed > 1200)
        {
            var scale = 1200 / speed;
            _dragVelocityX *= scale;
            _dragVelocityY *= scale;
        }
        _inertial = true;
        _transientState = _dragVelocityX >= 0 ? "running-right" : "running-left";
        _autoClearTransient = false;
        _inertiaStepClock.Restart();
        RestartAnimation();
    }

    private void AdvanceInertia()
    {
        var elapsed = Math.Min(0.05, _inertiaStepClock.Elapsed.TotalSeconds);
        _inertiaStepClock.Restart();
        if (elapsed <= 0) return;

        var area = DisplayGeometry.ForWindow(this).WorkArea;
        var nextLeft = Left + _dragVelocityX * elapsed;
        var nextTop = Top + _dragVelocityY * elapsed;
        var maxLeft = Math.Max(area.Left, area.Right - PetWidth);
        var maxTop = Math.Max(area.Top, area.Bottom - PetHeight);
        var clampedLeft = Math.Clamp(nextLeft, area.Left, maxLeft);
        var clampedTop = Math.Clamp(nextTop, area.Top, maxTop);
        if (Math.Abs(clampedLeft - nextLeft) > 0.01) _dragVelocityX = 0;
        if (Math.Abs(clampedTop - nextTop) > 0.01) _dragVelocityY = 0;
        Left = clampedLeft;
        Top = clampedTop;

        var friction = Math.Exp(-4.2 * elapsed);
        _dragVelocityX *= friction;
        _dragVelocityY *= friction;
        if (Math.Sqrt(_dragVelocityX * _dragVelocityX + _dragVelocityY * _dragVelocityY) < 25)
            StopInertia();
    }

    private void StopInertia(bool restartAnimation = true)
    {
        if (!_inertial) return;
        _inertial = false;
        _inertiaStepClock.Reset();
        _dragVelocityX = 0;
        _dragVelocityY = 0;
        _transientState = null;
        _autoClearTransient = false;
        _idleActionClock.Restart();
        ScheduleNextRoam();
        if (restartAnimation) RestartAnimation();
    }

    private void PetWindow_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("GuguPet.Cookie") || e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void PetWindow_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        _inactivityClock.Restart();
        if (e.Data.GetDataPresent("GuguPet.Cookie"))
        {
            PlayTransient("cookie", autoClear: true);
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            FilesDropped?.Invoke(files);
            PlayTransient("review", autoClear: true);
            e.Handled = true;
        }
    }

    private void NewCodexTask_OnClick(object sender, RoutedEventArgs e) =>
        NewCodexTaskRequested?.Invoke(this, EventArgs.Empty);

    private void RecentCodexTask_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: CodexTaskSummary task })
            RecentCodexTaskRequested?.Invoke(task);
    }

    private void FeedCookie_OnClick(object sender, RoutedEventArgs e) =>
        PlayTransient("cookie", autoClear: true);

    private void OpenControls_OnClick(object sender, RoutedEventArgs e) =>
        OpenControlsRequested?.Invoke(this, EventArgs.Empty);

    private void ResizeGrip_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StopRoaming();
        StopInertia(restartAnimation: false);
        _inactivityClock.Restart();
        if (!GetCursorPos(out var cursor)) return;
        _resizing = true;
        _resizeStartWidth = Width;
        _resizeStartCursorX = cursor.X;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_resizing || !GetCursorPos(out var cursor)) return;
        SetPetWidth(_resizeStartWidth + cursor.X - _resizeStartCursorX);
        e.Handled = true;
    }

    private void ResizeGrip_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ScheduleNextRoam()
    {
        _nextRoamDelaySeconds = _random.NextDouble() * 6 + 4;
        _roamClock.Restart();
    }

    private void ScheduleNextEdgeAction()
    {
        _nextEdgeActionSeconds = 75 + _random.NextDouble() * 75;
        _edgeActionClock.Restart();
    }

    private void StartEdgeAction()
    {
        var display = DisplayGeometry.ForWindow(this);
        _edgeWorkArea = display.WorkArea;
        _edgeActionEdge = ChooseEdge(display.ExposedEdges);
        _edgeActionStage = EdgeActionStage.Approach;
        var target = EdgePosition(_edgeWorkArea, _edgeActionEdge);
        StartRoamingTo(target.Left, target.Top, _edgeWorkArea);
    }

    private void BeginEdgePeek()
    {
        _roaming = false;
        _roamStepClock.Reset();
        var target = EdgePosition(_edgeWorkArea, _edgeActionEdge);
        // The former peek offset hid roughly half the pet. On an internal seam
        // that half appeared on the neighbouring display. Keep the full window
        // inside the chosen display while the waiting/rest poses play.
        Left = target.Left;
        Top = target.Top;
        _edgeActionStage = EdgeActionStage.Peek;
        _transientState = "waiting";
        _autoClearTransient = false;
        _edgeActionClock.Restart();
        RestartAnimation();
    }

    private void AdvanceEdgeAction()
    {
        if (_edgeActionStage == EdgeActionStage.Peek && _edgeActionClock.Elapsed.TotalSeconds >= 2.6)
        {
            _edgeActionStage = EdgeActionStage.Rest;
            _transientState = "sleep-prone";
            _edgeActionClock.Restart();
            RestartAnimation();
        }
        else if (_edgeActionStage == EdgeActionStage.Rest && _edgeActionClock.Elapsed.TotalSeconds >= 4.2)
        {
            _edgeActionStage = EdgeActionStage.Return;
            var target = EdgePosition(_edgeWorkArea, _edgeActionEdge);
            StartRoamingTo(target.Left, target.Top, _edgeWorkArea);
        }
    }

    private void CompleteEdgeAction()
    {
        _roaming = false;
        _roamStepClock.Reset();
        _edgeActionStage = EdgeActionStage.None;
        _transientState = null;
        _autoClearTransient = false;
        _idleActionClock.Restart();
        ScheduleNextRoam();
        ScheduleNextEdgeAction();
        RestartAnimation();
    }

    private void CancelEdgeAction(bool restartAnimation = true)
    {
        if (_edgeActionStage == EdgeActionStage.None) return;
        _edgeActionStage = EdgeActionStage.None;
        _roaming = false;
        _roamStepClock.Reset();
        var area = _edgeWorkArea.IsEmpty
            ? DisplayGeometry.ForWindow(this).WorkArea
            : _edgeWorkArea;
        var position = DisplayGeometry.ClampRect(area, PetWidth, PetHeight, Left, Top);
        Left = position.Left;
        Top = position.Top;
        _transientState = null;
        _autoClearTransient = false;
        ScheduleNextRoam();
        ScheduleNextEdgeAction();
        if (restartAnimation) RestartAnimation();
    }

    private DisplayEdge ChooseEdge(IReadOnlyList<DisplayEdge> exposedEdges)
    {
        var horizontal = exposedEdges
            .Where(edge => edge is DisplayEdge.Left or DisplayEdge.Right)
            .ToArray();
        if (horizontal.Length > 0)
            return horizontal[_random.Next(horizontal.Length)];

        // A monitor in the middle of a horizontal row has no exposed side.
        // Its bottom edge is the most natural fallback for the prone rest pose.
        if (exposedEdges.Contains(DisplayEdge.Bottom)) return DisplayEdge.Bottom;
        if (exposedEdges.Contains(DisplayEdge.Top)) return DisplayEdge.Top;
        return _random.Next(2) == 0 ? DisplayEdge.Left : DisplayEdge.Right;
    }

    private Rect EdgePosition(Rect area, DisplayEdge edge)
    {
        var position = DisplayGeometry.ClampRect(area, PetWidth, PetHeight, Left, Top);
        return edge switch
        {
            DisplayEdge.Left => new Rect(area.Left, position.Top, position.Width, position.Height),
            DisplayEdge.Right => new Rect(Math.Max(area.Left, area.Right - PetWidth), position.Top, position.Width, position.Height),
            DisplayEdge.Top => new Rect(position.Left, area.Top, position.Width, position.Height),
            DisplayEdge.Bottom => new Rect(position.Left, Math.Max(area.Top, area.Bottom - PetHeight), position.Width, position.Height),
            _ => position
        };
    }

    private bool TryGetActiveGazeCursor(out POINT cursor)
    {
        if (!GetCursorPos(out cursor))
            return false;

        if (!_hasGazeCursorSample)
        {
            _lastGazeCursor = cursor;
            _hasGazeCursorSample = true;
            return false;
        }

        var dx = cursor.X - _lastGazeCursor.X;
        var dy = cursor.Y - _lastGazeCursor.Y;
        if (dx * dx + dy * dy >= 16)
        {
            _lastGazeCursor = cursor;
            _gazeHoldClock.Restart();
        }

        if (!_gazeHoldClock.IsRunning || _gazeHoldClock.Elapsed.TotalSeconds > _gazeHoldSeconds)
        {
            _gazeHoldClock.Reset();
            return false;
        }
        return true;
    }

    private bool TryGetCursorNearFeet(out System.Windows.Point localCursor)
    {
        localCursor = default;
        if (!GetCursorPos(out var cursor)) return false;
        localCursor = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        return localCursor.X >= -ActualWidth * 0.25 &&
               localCursor.X <= ActualWidth * 1.25 &&
               localCursor.Y >= ActualHeight * 0.7 &&
               localCursor.Y <= ActualHeight + 48;
    }

    private void ResetGazeTracking()
    {
        _gazeHoldClock.Reset();
        _hasGazeCursorSample = false;
    }

    private void StartRoaming()
    {
        var area = DisplayGeometry.ForWindow(this).WorkArea;
        var maxLeft = Math.Max(area.Left, area.Right - PetWidth);
        var maxTop = Math.Max(area.Top, area.Bottom - PetHeight);

        var targetLeft = Left;
        var targetTop = Top;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            targetLeft = area.Left + _random.NextDouble() * Math.Max(1, maxLeft - area.Left);
            targetTop = area.Top + _random.NextDouble() * Math.Max(1, maxTop - area.Top);
            if (Math.Sqrt(Math.Pow(targetLeft - Left, 2) + Math.Pow(targetTop - Top, 2)) >= 120)
                break;
        }

        StartRoamingTo(targetLeft, targetTop, area);
    }

    private void StartRoamingTo(double targetLeft, double targetTop, Rect? workArea = null)
    {
        var area = workArea ?? DisplayGeometry.ForWindow(this).WorkArea;
        _roamTargetLeft = Math.Clamp(targetLeft, area.Left, Math.Max(area.Left, area.Right - PetWidth));
        _roamTargetTop = Math.Clamp(targetTop, area.Top, Math.Max(area.Top, area.Bottom - PetHeight));
        var direction = _roamTargetLeft >= Left ? "running-right" : "running-left";
        _roaming = true;
        ResetGazeTracking();
        _transientState = direction;
        _autoClearTransient = false;
        _roamStepClock.Restart();
        RestartAnimation();
    }

    private bool TryStartFastCursorChase()
    {
        if (!GetCursorPos(out var cursor)) return false;
        if (!_hasCursorSpeedSample)
        {
            _lastSpeedCursor = cursor;
            _hasCursorSpeedSample = true;
            _cursorSpeedClock.Restart();
            return false;
        }

        var elapsed = _cursorSpeedClock.Elapsed.TotalSeconds;
        if (elapsed < 0.05) return false;
        var dx = cursor.X - _lastSpeedCursor.X;
        var dy = cursor.Y - _lastSpeedCursor.Y;
        var speed = Math.Sqrt(dx * dx + dy * dy) / elapsed;
        _lastSpeedCursor = cursor;
        _cursorSpeedClock.Restart();
        if (speed < 1250 || _chaseCooldownClock.Elapsed.TotalSeconds < 4) return false;

        var local = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        var targetLeft = Left + local.X - ActualWidth / 2;
        var targetTop = Top + local.Y - ActualHeight * 0.75;
        if (Math.Sqrt(Math.Pow(targetLeft - Left, 2) + Math.Pow(targetTop - Top, 2)) < 100)
            return false;
        _chaseCooldownClock.Restart();
        _inactivityClock.Restart();
        StartRoamingTo(targetLeft, targetTop);
        return true;
    }

    private void AdvanceRoaming()
    {
        var elapsed = Math.Min(0.05, _roamStepClock.Elapsed.TotalSeconds);
        _roamStepClock.Restart();

        var dx = _roamTargetLeft - Left;
        var dy = _roamTargetTop - Top;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var step = _roamSpeed * elapsed;
        if (distance <= Math.Max(1, step))
        {
            Left = _roamTargetLeft;
            Top = _roamTargetTop;
            if (_edgeActionStage == EdgeActionStage.Approach)
            {
                BeginEdgePeek();
                return;
            }
            if (_edgeActionStage == EdgeActionStage.Return)
            {
                CompleteEdgeAction();
                return;
            }
            StopRoaming();
            return;
        }

        Left += dx / distance * step;
        Top += dy / distance * step;
    }

    private void StopRoaming()
    {
        if (_edgeActionStage != EdgeActionStage.None)
        {
            CancelEdgeAction();
            return;
        }
        if (!_roaming) return;
        _roaming = false;
        _roamStepClock.Reset();
        _transientState = null;
        _autoClearTransient = false;
        _idleActionClock.Restart();
        ScheduleNextRoam();
        RestartAnimation();
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
