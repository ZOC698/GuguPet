using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuguPet;

/// <summary>Read-only, filesystem-event-driven observer of Antigravity's local state and public transcript.</summary>
public sealed class AntigravityActivityWatcher : IDisposable
{
    private readonly string _directory;
    private readonly Action<CodexActivityState> _onChanged;
    private readonly Func<bool>? _isRunning;
    private readonly object _gate = new();
    private readonly AntigravityActivityTracker _tracker = new();
    private readonly Dictionary<int, Process> _processes = new();
    private readonly Dictionary<string, (long Length, DateTime Modified, AntigravityTranscript Snapshot)> _transcripts = new();
    private readonly System.Threading.Timer _debounce;
    private readonly System.Threading.Timer _settle;
    private FileSystemWatcher? _watcher;
    private string? _signature;
    private bool _disposed;
    private int _readRetries;
    private int _scanQueued;

    public AntigravityActivityWatcher(string directory, Action<CodexActivityState> onChanged, Func<bool>? isRunning = null)
    {
        _directory = Path.GetFullPath(directory);
        _onChanged = onChanged;
        _isRunning = isRunning;
        _debounce = new System.Threading.Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite);
        _settle = new System.Threading.Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite);
        BindWatcher();
        QueueScan(0);
    }

    private void BindWatcher()
    {
        _watcher?.Dispose();
        var parent = Path.GetDirectoryName(_directory)!;
        var parentExists = Directory.Exists(parent);
        var root = Directory.Exists(_directory) ? _directory : parentExists ? parent : Path.GetDirectoryName(parent);
        if (root is not null && Directory.Exists(root))
        {
            _watcher = new FileSystemWatcher(root)
            {
                Filter = parentExists ? "*" : Path.GetFileName(parent),
                IncludeSubdirectories = parentExists,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.Error += (_, _) => QueueScan();
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // A new device may launch Gugu before Antigravity has created .gemini.
        // Watch only that directory name until the integration root appears.
        if (e.FullPath.Equals(Path.GetDirectoryName(_directory), StringComparison.OrdinalIgnoreCase) && Directory.Exists(e.FullPath))
        {
            lock (_gate) { if (!_disposed) BindWatcher(); }
            QueueScan();
            return;
        }
        if (!e.FullPath.StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !e.FullPath.Equals(_directory, StringComparison.OrdinalIgnoreCase)) return;
        var name = Path.GetFileName(e.FullPath);
        if (name.StartsWith("conversation_summaries.db", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("transcript.jsonl", StringComparison.OrdinalIgnoreCase) || e.ChangeType != WatcherChangeTypes.Changed)
            QueueScan();
    }

    private void QueueScan(int delay = 180)
    {
        if (_disposed || Interlocked.CompareExchange(ref _scanQueued, 1, 0) != 0) return;
        try { _debounce.Change(delay, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    private bool ObserveProcess()
    {
        if (_isRunning is not null) return _isRunning();
        var running = Process.GetProcessesByName("Antigravity");
        foreach (var process in running)
        {
            if (_processes.ContainsKey(process.Id)) { process.Dispose(); continue; }
            try
            {
                process.Exited += (_, _) => QueueScan();
                process.EnableRaisingEvents = true;
                _processes[process.Id] = process;
            }
            catch (InvalidOperationException) { process.Dispose(); }
            catch (System.ComponentModel.Win32Exception) { process.Dispose(); }
        }
        foreach (var id in _processes.Keys.ToArray())
        {
            try { if (!_processes[id].HasExited) continue; }
            catch (InvalidOperationException) { }
            _processes[id].Dispose();
            _processes.Remove(id);
        }
        return _processes.Count > 0;
    }

    private void Scan()
    {
        Interlocked.Exchange(ref _scanQueued, 0);
        lock (_gate)
        {
            if (_disposed) return;
            var appRunning = ObserveProcess();
            var database = Path.Combine(_directory, "conversation_summaries.db");
            try
            {
                var summaries = File.Exists(database) ? AntigravitySummaryReader.Read(database) : Array.Empty<AntigravitySummary>();
                var snapshots = summaries.Select(summary => (summary, ReadTranscript(summary))).ToArray();
                var state = _tracker.Update(snapshots, appRunning, DateTimeOffset.UtcNow);
                _readRetries = 0;
                Publish(state);
                var next = _tracker.NextExpiry;
                _settle.Change(next is null ? Timeout.Infinite : (int)Math.Clamp((next.Value - DateTimeOffset.UtcNow).TotalMilliseconds + 20, 1, int.MaxValue), Timeout.Infinite);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or DllNotFoundException or EntryPointNotFoundException)
            {
                // A writer may briefly lock or replace its DB. Retry finitely; never
                // start a periodic disk/process scan or turn a lock into a task error.
                if (_readRetries++ < 3) QueueScan(500);
                if (!appRunning || _signature is null)
                    Publish(new CodexActivityState("idle", LocalizationService.T("Antigravity 已待命"), DateTimeOffset.UtcNow, "", Array.Empty<CodexTaskSummary>(), "antigravity"));
            }
        }
    }

    private AntigravityTranscript ReadTranscript(AntigravitySummary summary)
    {
        if (!Guid.TryParse(summary.Id, out _)) return AntigravityTranscript.Empty;
        var path = Path.Combine(_directory, "brain", summary.Id, ".system_generated", "logs", "transcript.jsonl");
        if (!File.Exists(path)) return AntigravityTranscript.Empty;
        var info = new FileInfo(path);
        if (_transcripts.TryGetValue(summary.Id, out var cached) && cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc)
            return cached.Snapshot;
        var snapshot = AntigravityTranscript.Read(path);
        _transcripts[summary.Id] = (info.Length, info.LastWriteTimeUtc, snapshot);
        return snapshot;
    }

    private void Publish(CodexActivityState state)
    {
        var signature = state.State + "|" + string.Join('|', state.Tasks.Select(task => $"{task.ThreadId}:{task.State}:{task.UpdatedAt:O}:{task.Title}:{task.Message}"));
        if (signature == _signature) return;
        _signature = signature;
        _onChanged(state);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _debounce.Dispose();
            _settle.Dispose();
            foreach (var process in _processes.Values) process.Dispose();
            _processes.Clear();
        }
    }
}

internal sealed record AntigravitySummary(string Id, string Title, string Preview, string Status, bool NotFullyIdle, bool Killed, DateTimeOffset UpdatedAt, int LastInputStep)
{
    public bool Running => !Killed && (NotFullyIdle || Status.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) || Status.Contains("WAITING", StringComparison.OrdinalIgnoreCase));
}

internal static class AntigravitySummaryReader
{
    public static AntigravitySummary[] Read(string path)
    {
        // Windows ships winsqlite3. Open READONLY and honour the live WAL;
        // neither credentials nor raw protobuf payloads are queried.
        using var database = new ReadOnlySqlite(path);
        return database.Query("SELECT conversation_id,title,preview,status,not_fully_idle,killed,last_modified_time,last_user_input_step_index FROM conversation_summaries WHERE nesting_depth=0 AND parent_conversation_id='' ORDER BY not_fully_idle DESC,last_modified_time DESC LIMIT 64")
            .Select(row => new AntigravitySummary(row[0] ?? "", row[1] ?? "", row[2] ?? "", row[3] ?? "", row[4] == "1", row[5] == "1",
                DateTimeOffset.TryParse(row[6], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : DateTimeOffset.MinValue,
                int.TryParse(row[7], out var step) ? step : -1)).ToArray();
    }
}

internal sealed class ReadOnlySqlite : IDisposable
{
    private IntPtr _database;
    public ReadOnlySqlite(string path)
    {
        if (sqlite3_open_v2(path, out _database, 1 | 0x8000, IntPtr.Zero) != 0)
        { Dispose(); throw new IOException("Antigravity state database is unavailable."); }
        sqlite3_busy_timeout(_database, 300);
    }
    public List<string?[]> Query(string sql)
    {
        if (sqlite3_prepare_v2(_database, sql, -1, out var statement, IntPtr.Zero) != 0)
            throw new IOException("Antigravity state schema is unavailable.");
        try
        {
            var rows = new List<string?[]>();
            int result;
            while ((result = sqlite3_step(statement)) == 100)
            {
                var row = new string?[sqlite3_column_count(statement)];
                for (var i = 0; i < row.Length; i++)
                    row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i));
                rows.Add(row);
            }
            if (result != 101) throw new IOException("Antigravity state database is busy.");
            return rows;
        }
        finally { sqlite3_finalize(statement); }
    }
    public void Dispose() { if (_database != IntPtr.Zero) sqlite3_close(_database); _database = IntPtr.Zero; }
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
}

internal sealed record AntigravityTranscript(string PublicMessage, int MessageStep, DateTimeOffset MessageAt, bool Waiting, bool Error, int LastStep = -1)
{
    public static readonly AntigravityTranscript Empty = new("", -1, DateTimeOffset.MinValue, false, false);
    public static AntigravityTranscript Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, file.Length - 512 * 1024);
        file.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(file, Encoding.UTF8);
        if (start > 0) reader.ReadLine();
        return Parse(ReadLines(reader));
    }
    private static IEnumerable<string> ReadLines(StreamReader reader)
    { string? line; while ((line = reader.ReadLine()) is not null) yield return line; }

    internal static AntigravityTranscript Parse(IEnumerable<string> lines)
    {
        var message = "";
        var step = -1;
        var time = DateTimeOffset.MinValue;
        var waiting = false;
        var error = false;
        var lastStep = -1;
        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("step_index", out var eventIndex) && eventIndex.TryGetInt32(out var eventStep)) lastStep = Math.Max(lastStep, eventStep);
                if (!root.TryGetProperty("type", out var typeValue)) continue;
                var type = typeValue.GetString();
                var source = root.TryGetProperty("source", out var sourceValue) ? sourceValue.GetString() : "";
                var status = root.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : "";
                if (type == "USER_INPUT") { message = ""; step = -1; waiting = error = false; }
                if (type == "PLANNER_RESPONSE" && source == "MODEL")
                {
                    waiting = error = false;
                    // 'thinking', tool arguments, SYSTEM_MESSAGE, GENERIC results and
                    // other internal fields deliberately never reach the bubble.
                    if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        var candidate = Clean(content.GetString(), 180);
                        if (candidate.Length > 0)
                        {
                            message = candidate;
                            step = root.TryGetProperty("step_index", out var index) && index.TryGetInt32(out var n) ? n : -1;
                            if (root.TryGetProperty("created_at", out var at)) DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time);
                        }
                    }
                    if (root.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
                        waiting = calls.EnumerateArray().Any(call => call.TryGetProperty("name", out var name) && name.GetString() is
                            "ask_permission" or "ask_user" or "request_user_input" or "ask_question" or "notify_user" or "request_review");
                }
                else if (type is "ERROR_MESSAGE" || status == "ERROR") error = true;
                else if (type == "GENERIC" && status == "DONE") waiting = false;
            }
            catch (Exception errorValue) when (errorValue is JsonException or InvalidOperationException)
            { /* A partial or unsupported final line is retried on the next event. */ }
        }
        return new(message, step, time, waiting, error, lastStep);
    }
    internal static string Clean(string? text, int limit)
    {
        var value = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        return value.Length <= limit ? value : value[..limit] + "…";
    }
}

internal sealed class AntigravityActivityTracker
{
    private sealed record Observed(bool Running, string Reaction, DateTimeOffset? Until);
    private readonly Dictionary<string, Observed> _observed = new();
    public DateTimeOffset? NextExpiry => _observed.Values.Where(v => v.Until is not null).Select(v => v.Until).Min();

    public CodexActivityState Update(IEnumerable<(AntigravitySummary Summary, AntigravityTranscript Transcript)> snapshots, bool appRunning, DateTimeOffset now)
    {
        var tasks = new List<CodexTaskSummary>();
        var seen = new HashSet<string>();
        foreach (var (summary, transcript) in snapshots)
        {
            seen.Add(summary.Id);
            _observed.TryGetValue(summary.Id, out var previous);
            var running = appRunning && summary.Running;
            var reaction = previous?.Reaction ?? "idle";
            var until = previous?.Until;
            if (!appRunning || until <= now) { reaction = "idle"; until = null; }
            if (running) { reaction = "idle"; until = null; }
            else if (appRunning && previous?.Running == true)
            {
                reaction = summary.Killed ? "interrupted" : transcript.Error && transcript.LastStep >= summary.LastInputStep ? "failed" : "review";
                until = now.AddSeconds(summary.Killed ? 2.2 : 8);
            }
            _observed[summary.Id] = new(running, reaction, until);
            var state = running ? (summary.Status.Contains("WAITING", StringComparison.OrdinalIgnoreCase) || (transcript.Waiting && transcript.LastStep >= summary.LastInputStep) ? "waiting" : "running") : reaction;
            var publicMessage = transcript.MessageStep >= summary.LastInputStep ? transcript.PublicMessage : "";
            var message = state switch
            {
                "interrupted" => LocalizationService.T("Antigravity 任务已中止"),
                "waiting" when publicMessage.Length == 0 => LocalizationService.T("Antigravity 需要你的输入或批准"),
                "failed" => LocalizationService.T("Antigravity 任务出现错误"),
                "running" when publicMessage.Length == 0 => LocalizationService.T("Antigravity 正在处理任务"),
                "review" when publicMessage.Length == 0 => LocalizationService.T("Antigravity 已完成任务"),
                "idle" when publicMessage.Length == 0 => LocalizationService.T("Antigravity 已待命"),
                _ => publicMessage
            };
            var title = AntigravityTranscript.Clean(string.IsNullOrWhiteSpace(summary.Title) ? summary.Preview : summary.Title, 72);
            if (title.Length == 0) title = LocalizationService.T("Antigravity 任务");
            var updatedAt = transcript.MessageAt > summary.UpdatedAt ? transcript.MessageAt : summary.UpdatedAt;
            tasks.Add(new(summary.Id, title, state, StateLabel(state), message, updatedAt, "antigravity"));
        }
        foreach (var id in _observed.Keys.Where(id => !seen.Contains(id)).ToArray()) _observed.Remove(id);
        var ordered = tasks.OrderBy(task => Priority(task.State)).ThenByDescending(task => task.UpdatedAt).Take(8).ToArray();
        var focus = ordered.FirstOrDefault();
        return new(focus?.State ?? "idle", focus?.Message ?? LocalizationService.T("Antigravity 已待命"), focus?.UpdatedAt ?? now, focus?.ThreadId ?? "", ordered, "antigravity");
    }
    private static int Priority(string state) => state switch { "waiting" => 0, "failed" => 1, "running" => 2, "interrupted" => 3, "review" => 4, _ => 5 };
    private static string StateLabel(string state) => LocalizationService.T(state switch { "running" => "运行中", "waiting" => "需要输入", "failed" => "出错了", "review" => "已完成", "interrupted" => "已中止", _ => "已待命" });
}
