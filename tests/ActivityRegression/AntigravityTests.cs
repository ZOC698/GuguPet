using System.Runtime.InteropServices;
using System.Text.Json;
using GuguPet;

internal static class AntigravityTests
{
    public static async Task Run(string directory, bool live)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString();
        string Line(string type, string content = "", string status = "DONE", object? calls = null, string source = "MODEL", int step = 101) =>
            JsonSerializer.Serialize(new { type, source, content, status, thinking = "PRIVATE-REASONING", tool_calls = calls, step_index = step, created_at = now });
        void Check(string name, bool success) { if (!success) throw new Exception(name); Console.WriteLine("PASS antigravity-" + name); }
        var transcript = AntigravityTranscript.Parse(new[]
        {
            Line("SYSTEM_MESSAGE", "PRIVATE-SYSTEM", source: "SYSTEM"),
            Line("PLANNER_RESPONSE", "公开回复：测试消息"),
            Line("GENERIC", "PRIVATE-TOOL-RESULT"), "{unfinished"
        });
        Check("public-content-only", transcript.PublicMessage == "公开回复：测试消息" && !transcript.PublicMessage.Contains("PRIVATE"));
        Check("new-input-clears-old-message", AntigravityTranscript.Parse(new[] { Line("PLANNER_RESPONSE", "old"), Line("USER_INPUT", "new", source: "USER_EXPLICIT", step: 102) }).PublicMessage == "");
        var pending = AntigravityTranscript.Parse(new[] { Line("PLANNER_RESPONSE", calls: new[] { new { name = "ask_permission" } }) });
        Check("approval-wait", pending.Waiting);
        Check("approval-resolved", !AntigravityTranscript.Parse(new[] { Line("PLANNER_RESPONSE", calls: new[] { new { name = "ask_permission" } }), Line("GENERIC", status: "DONE") }).Waiting);
        var summary = new AntigravitySummary(id, "测试任务", "", "CASCADE_RUN_STATUS_RUNNING", true, false, now.AddHours(-4), 100);
        var tracker = new AntigravityActivityTracker();
        var state = tracker.Update(new[] { (summary, transcript) }, true, now);
        Check("long-task-stays-running", state.State == "running" && state.SourceLabel == "Antigravity");
        state = tracker.Update(new[] { (summary, transcript) }, true, now.AddHours(2));
        Check("no-running-timeout", state.State == "running");
        var idle = summary with { Status = "CASCADE_RUN_STATUS_IDLE", NotFullyIdle = false };
        state = tracker.Update(new[] { (idle, transcript) }, true, now.AddHours(2));
        Check("completion-transition", state.State == "review");
        state = tracker.Update(new[] { (idle, transcript) }, true, now.AddHours(2).AddSeconds(9));
        Check("completion-settles", state.State == "idle");
        Check("historical-idle-not-replayed", new AntigravityActivityTracker().Update(new[] { (idle, transcript) }, true, now).State == "idle");
        tracker.Update(new[] { (summary, transcript) }, true, now);
        state = tracker.Update(new[] { (idle with { Killed = true }, transcript) }, true, now);
        Check("manual-stop", state.State == "interrupted");
        Check("stop-settles", tracker.Update(new[] { (idle with { Killed = true }, transcript) }, true, now.AddSeconds(3)).State == "idle");
        Check("closed-app-clears-running", tracker.Update(new[] { (summary, transcript) }, false, now).State == "idle");
        Check("waiting-state", tracker.Update(new[] { (summary, pending) }, true, now).State == "waiting");
        var error = AntigravityTranscript.Parse(new[] { Line("ERROR_MESSAGE", "error", "ERROR") });
        Check("failure-transition", tracker.Update(new[] { (idle, error) }, true, now).State == "failed");

        var agRoot = Path.Combine(directory, "antigravity");
        Directory.CreateDirectory(agRoot);
        var dbPath = Path.Combine(agRoot, "conversation_summaries.db");
        using var db = new FixtureDatabase(dbPath);
        db.Exec("PRAGMA journal_mode=WAL; CREATE TABLE conversation_summaries(conversation_id TEXT,title TEXT,preview TEXT,status TEXT,not_fully_idle INTEGER,killed INTEGER,last_modified_time TEXT,last_user_input_step_index INTEGER,nesting_depth INTEGER,parent_conversation_id TEXT);");
        db.Exec($"INSERT INTO conversation_summaries VALUES('{id}','测试任务・テスト','preview','CASCADE_RUN_STATUS_RUNNING',1,0,'{now:O}',100,0,''); INSERT INTO conversation_summaries VALUES('child','child','','CASCADE_RUN_STATUS_RUNNING',1,0,'{now:O}',0,1,'{id}');");
        var rows = AntigravitySummaryReader.Read(dbPath);
        Check("live-wal-readonly-and-unicode", rows.Length == 1 && rows[0].Title == "测试任务・テスト");
        var transcriptPath = Path.Combine(agRoot, "brain", id, ".system_generated", "logs", "transcript.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
        File.WriteAllLines(transcriptPath, new[] { Line("PLANNER_RESPONSE", "公开回复：测试消息") });
        var received = new TaskCompletionSource<CodexActivityState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<CodexActivityState>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var watcher = new AntigravityActivityWatcher(agRoot, activity =>
        {
            if (activity.State == "running") received.TrySetResult(activity);
            if (activity.State == "review") completed.TrySetResult(activity);
        }, () => true))
        {
            var active = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check("filesystem-baseline", active.Message == "公开回复：测试消息");
            db.Exec($"UPDATE conversation_summaries SET status='CASCADE_RUN_STATUS_IDLE',not_fully_idle=0 WHERE conversation_id='{id}';");
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check("wal-event-delivers-completion", true);
        }
        var codex = new CodexActivityState("idle", "", now, id, new[] { new CodexTaskSummary(id, "Codex", "idle", "", "", now) });
        var dsh = codex with { Source = "dsh", Tasks = new[] { codex.Tasks[0] with { Source = "dsh" } } };
        var ag = new CodexActivityState("running", "reply", now, id, new[] { codex.Tasks[0] with { Source = "antigravity", State = "running" } }, "antigravity");
        var merged = ActivityStateMerger.Merge(codex, dsh, ag);
        Check("three-sources-retained", merged.Tasks.Count == 3 && merged.Source == "antigravity");

        if (live)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity");
            var actual = AntigravitySummaryReader.Read(Path.Combine(root, "conversation_summaries.db"));
            var current = actual.FirstOrDefault(row => row.Running) ?? actual.FirstOrDefault();
            var content = current is null ? AntigravityTranscript.Empty : AntigravityTranscript.Read(Path.Combine(root, "brain", current.Id, ".system_generated", "logs", "transcript.jsonl"));
            Check("installed-antigravity-read", actual.Length > 0);
            Console.WriteLine($"LIVE Antigravity top-level={actual.Length}, running={actual.Count(row => row.Running)}, public-message-characters={content.PublicMessage.Length}");
        }
    }

    private sealed class FixtureDatabase : IDisposable
    {
        private readonly IntPtr _handle;
        public FixtureDatabase(string path)
        { if (sqlite3_open_v2(path, out _handle, 2 | 4, IntPtr.Zero) != 0) throw new Exception("Fixture DB open failed"); }
        public void Exec(string sql)
        {
            if (sqlite3_exec(_handle, sql, IntPtr.Zero, IntPtr.Zero, out var error) == 0) return;
            var message = Marshal.PtrToStringUTF8(error);
            sqlite3_free(error);
            throw new Exception(message);
        }
        public void Dispose() => sqlite3_close(_handle);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr context, out IntPtr error);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr data);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    }
}
