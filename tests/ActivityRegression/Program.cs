using System.Reflection;
using System.Text.Json;
using GuguPet;

var directory = Path.Combine(Path.GetTempPath(), "GuguPet-activity-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    using var watcher = new CodexActivityWatcher(Path.Combine(directory, "absent"), _ => { });
    var read = typeof(CodexActivityWatcher).GetMethod("ReadSessionSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
    string Event(string type, string? turn = "a", string envelope = "event_msg", string? message = null) =>
        JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, type = envelope,
            payload = new { type, turn_id = turn, message, phase = "commentary", reason = "interrupted" } });
    void Check(string name, string expected, params string[] lines)
    {
        var path = Path.Combine(directory, name + ".jsonl");
        File.WriteAllLines(path, lines);
        var snapshot = read.Invoke(watcher, new object?[] { new FileInfo(path), "Regression" })!;
        var actual = (string)snapshot.GetType().GetProperty("State")!.GetValue(snapshot)!;
        if (actual != expected) throw new Exception($"{name}: expected {expected}, got {actual}");
        Console.WriteLine($"PASS {name}");
    }
    Check("manual-stop", "interrupted", Event("task_started"), Event("turn_aborted"));
    Check("stop-while-waiting", "interrupted", Event("task_started"), Event("approval_request"), Event("turn_aborted"));
    Check("buffered-progress", "interrupted", Event("task_started"), Event("turn_aborted"), Event("agent_message", message: "late"), Event("task_complete"));
    Check("tail-without-start", "interrupted", Event("turn_aborted"), Event("agent_message", message: "late"));
    Check("new-turn", "running", Event("task_started"), Event("turn_aborted"), Event("task_started", "b"));
    Check("new-user-input", "running", Event("task_started"), Event("turn_aborted"), Event("user_message", null, message: "continue"));
    Check("late-old-stop", "running", Event("task_started", "a"), Event("task_started", "b"), Event("turn_aborted", "a"));
    Check("internal-context", "interrupted", Event("turn_aborted"), Event("user_message", null, message: "<environment_context>"));
    Check("ordinary-completion", "review", Event("task_started"), Event("task_complete"));
    Check("ordinary-error", "failed", Event("task_started"), Event("error"));
    Check("non-event-abort", "running", Event("task_started"), Event("turn_aborted", envelope: "response_item"));
    var idle = new CodexActivityState("idle", "", DateTimeOffset.UtcNow, "a", Array.Empty<CodexTaskSummary>());
    var running = idle with { State = "running", ThreadId = "b", Source = "dsh" };
    if (ActivityStateMerger.Merge(idle, running).State != "running") throw new Exception("Other active task was interrupted");
    Console.WriteLine("PASS other-active-source");

    var interruptedAnimation = AnimationCatalog.GetSequence("interrupted", reducedMotion: false);
    if (interruptedAnimation.LoopStartIndex != 8 || interruptedAnimation.Frames[0].Row != 19 ||
        interruptedAnimation.Frames[0].Sheet != SpriteSheetKind.IdleActions)
        throw new Exception("Interrupted animation is not a one-shot eight-frame idle-atlas row");
    Console.WriteLine("PASS interrupted-one-shot");

    foreach (var (state, row) in new[] { ("headphones", 20), ("drums", 21) })
    {
        var animation = AnimationCatalog.GetSequence(state, reducedMotion: false);
        if (!AnimationCatalog.IsIdleAction(state) || animation.Frames[0].Row != row ||
            animation.Frames[0].Sheet != SpriteSheetKind.IdleActions || animation.Frames.Count != 30)
            throw new Exception($"{state}: music action is not an eight-frame idle-atlas sequence");
        Console.WriteLine($"PASS {state}-idle-action");
    }

    var liveDirectory = Path.Combine(directory, "live");
    Directory.CreateDirectory(liveDirectory);
    File.WriteAllLines(Path.Combine(liveDirectory, "rollout-live.jsonl"),
        new[] { Event("task_started"), Event("turn_aborted") });
    using var settled = new ManualResetEventSlim();
    var observedInterrupted = false;
    using (var liveWatcher = new CodexActivityWatcher(liveDirectory, state =>
    {
        if (state.State == "interrupted") observedInterrupted = true;
        if (observedInterrupted && state.State == "idle") settled.Set();
    }))
    {
        if (!settled.Wait(TimeSpan.FromSeconds(5)))
            throw new Exception("Interrupted state did not settle to idle");
    }
    Console.WriteLine("PASS interrupted-settles");

    var musicState = new TaskCompletionSource<MusicPlaybackState>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using (var music = new MusicPlaybackService(state => musicState.TrySetResult(state)))
    {
        await music.SetEnabledAsync(true);
        var observed = await musicState.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (observed.Error is not null)
            throw new Exception($"Windows media-session bridge failed: {observed.Error}");
    }
    Console.WriteLine("PASS windows-media-session-bridge");
}
finally { Directory.Delete(directory, recursive: true); }

namespace GuguPet
{
    internal static class LocalizationService
    {
        public static string T(string value) => value;
    }
}
