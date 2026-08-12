using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuguPet;

public sealed record CodexTaskSummary(
    string ThreadId,
    string Title,
    string State,
    string StatusLabel,
    string Message,
    DateTimeOffset UpdatedAt);

public sealed record CodexActivityState(
    string State,
    string Message,
    DateTimeOffset UpdatedAt,
    string ThreadId,
    IReadOnlyList<CodexTaskSummary> Tasks);

public sealed class CodexActivityWatcher : IDisposable
{
    private const int TailBytes = 4 * 1024 * 1024;
    private const int TitleTailBytes = 24 * 1024 * 1024;
    private const int MaxTasks = 8;
    private const int MaxSessionCandidates = 64;
    private readonly string _sessionsDirectory;
    private readonly Action<CodexActivityState> _onChanged;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer _debounce;
    private readonly System.Threading.Timer _reviewTimer;
    private readonly System.Threading.Timer _pollTimer;
    private readonly Dictionary<string, CachedSnapshot> _snapshotCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastSignature;
    private CodexActivityState? _lastPublished;

    public CodexActivityWatcher(string sessionsDirectory, Action<CodexActivityState> onChanged)
    {
        _sessionsDirectory = sessionsDirectory;
        _onChanged = onChanged;
        _debounce = new System.Threading.Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite);
        _reviewTimer = new System.Threading.Timer(_ => SettleToIdle(), null, Timeout.Infinite, Timeout.Infinite);
        _pollTimer = new System.Threading.Timer(_ => QueueScan(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        if (Directory.Exists(sessionsDirectory))
        {
            _watcher = new FileSystemWatcher(sessionsDirectory, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) => QueueScan();
            _watcher.Created += (_, _) => QueueScan();
            _watcher.Renamed += (_, _) => QueueScan();
            Scan();
        }
        else
        {
            Publish(new CodexActivityState(
                "idle", LocalizationService.T("未找到 Codex 会话目录"), DateTimeOffset.Now, "", Array.Empty<CodexTaskSummary>()));
        }
    }

    private void QueueScan() => _debounce.Change(140, Timeout.Infinite);

    private void Scan()
    {
        try
        {
            var snapshots = new DirectoryInfo(_sessionsDirectory)
                .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(MaxSessionCandidates)
                .Select(ReadCachedSessionSnapshot)
                .Where(snapshot => snapshot is not null)
                .Cast<SessionSnapshot>()
                .Take(MaxTasks)
                .OrderByDescending(snapshot => snapshot.UpdatedAt)
                .ToList();

            var prioritized = snapshots
                .OrderBy(snapshot => StatePriority(snapshot.State))
                .ThenByDescending(snapshot => snapshot.UpdatedAt)
                .ToList();

            var tasks = prioritized.Select(snapshot => new CodexTaskSummary(
                snapshot.ThreadId,
                snapshot.Title,
                snapshot.State,
                StatusLabel(snapshot.State),
                snapshot.Message,
                snapshot.UpdatedAt)).ToList();

            // Put actionable work first: input/approval, blocked, running, then
            // recently completed and idle tasks. The bubble and task list use
            // this same order so the most important task is always first.
            var focus = prioritized.FirstOrDefault();
            if (focus is null)
            {
                Publish(new CodexActivityState(
                    "idle", LocalizationService.T("Codex 已待命"), DateTimeOffset.Now, "", tasks));
                return;
            }

            _reviewTimer.Change(Timeout.Infinite, Timeout.Infinite);
            var focusState = focus.State;
            if (focusState == "review" && DateTimeOffset.Now - focus.UpdatedAt > TimeSpan.FromSeconds(12))
                focusState = "idle";

            var message = focusState switch
            {
                "running" => string.IsNullOrWhiteSpace(focus.Message) ? LocalizationService.T("Codex 正在处理任务") : focus.Message,
                "waiting" => string.IsNullOrWhiteSpace(focus.Message) ? LocalizationService.T("Codex 需要你的输入") : focus.Message,
                "failed" => string.IsNullOrWhiteSpace(focus.Message) ? LocalizationService.T("Codex 任务出现错误") : focus.Message,
                "review" => string.IsNullOrWhiteSpace(focus.Message) ? LocalizationService.T("Codex 已完成任务") : focus.Message,
                _ => LocalizationService.T("Codex 已待命")
            };

            var state = new CodexActivityState(focusState, message, focus.UpdatedAt, focus.ThreadId, tasks);
            Publish(state);
            if (focusState == "review")
                _reviewTimer.Change(TimeSpan.FromSeconds(8), Timeout.InfiniteTimeSpan);
        }
        catch (IOException) { QueueScan(); }
        catch (UnauthorizedAccessException) { }
    }

    private SessionSnapshot? ReadCachedSessionSnapshot(FileInfo file)
    {
        if (_snapshotCache.TryGetValue(file.FullName, out var cached) &&
            cached.Length == file.Length && cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
            return cached.Snapshot;

        var previousTitle = _snapshotCache.TryGetValue(file.FullName, out var previous)
            ? previous.Snapshot.Title
            : null;
        var snapshot = ReadSessionSnapshot(file, previousTitle);
        if (snapshot is not null)
            _snapshotCache[file.FullName] = new CachedSnapshot(file.Length, file.LastWriteTimeUtc, snapshot);
        return snapshot;
    }

    private SessionSnapshot? ReadSessionSnapshot(FileInfo file, string? previousTitle)
    {
        if (IsSubagentSession(file)) return null;
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - TailBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 8192, leaveOpen: false);
        if (start > 0) reader.ReadLine();

        var threadId = ThreadIdFromName(file.Name);
        var title = !string.IsNullOrWhiteSpace(previousTitle) &&
                    !IsDefaultTaskTitle(previousTitle) &&
                    !IsInternalContextMessage(previousTitle)
            ? previousTitle
            : ReadRecentUserTitle(file) ?? LocalizationService.T("Codex 任务");
        var state = "idle";
        var message = "";
        var updatedAt = new DateTimeOffset(file.LastWriteTimeUtc);
        var lifecycleAt = DateTimeOffset.MinValue;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("payload", out var payload) ||
                    !payload.TryGetProperty("type", out var typeElement)) continue;

                var type = typeElement.GetString();
                var timestamp = ReadTimestamp(root, file.LastWriteTimeUtc);
                if (type is "request_user_input" or "approval_request" or "request_approval" or "approval_requested")
                {
                    if (timestamp < lifecycleAt) continue;
                    lifecycleAt = timestamp;
                    updatedAt = timestamp;
                    state = "waiting";
                    message = LocalizationService.T("Codex 需要你的输入或批准");
                }
                else if (type is "function_call" or "custom_tool_call" &&
                    payload.TryGetProperty("name", out var toolName) &&
                    toolName.GetString() is "request_user_input" or "ask_user" or "request_input" or "request_approval")
                {
                    if (timestamp < lifecycleAt) continue;
                    lifecycleAt = timestamp;
                    updatedAt = timestamp;
                    state = "waiting";
                    message = LocalizationService.T("Codex 需要你的输入");
                }
                else if (type is "function_call_output" or "custom_tool_call_output" && state == "waiting")
                {
                    lifecycleAt = timestamp;
                    updatedAt = timestamp;
                    state = "running";
                    message = LocalizationService.T("Codex 已收到输入，继续处理");
                }
                else if (type == "user_message" && payload.TryGetProperty("message", out var userMessage))
                {
                    var rawMessage = userMessage.GetString();
                    if (IsInternalContextMessage(rawMessage)) continue;
                    var candidate = Sanitize(rawMessage, 72);
                    if (!string.IsNullOrWhiteSpace(candidate)) title = candidate;
                }
                else if (type == "agent_message" &&
                         payload.TryGetProperty("phase", out var phase) &&
                         phase.GetString() is "commentary" or "final_answer" &&
                         payload.TryGetProperty("message", out var agentMessage))
                {
                    var candidate = Sanitize(agentMessage.GetString(), 150);
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        message = candidate;
                        updatedAt = timestamp;
                    }
                }
                else if (type is "task_started" or "task_complete" or "error")
                {
                    if (timestamp < lifecycleAt) continue;
                    lifecycleAt = timestamp;
                    updatedAt = timestamp;
                    state = type switch
                    {
                        "task_started" => "running",
                        "task_complete" => "review",
                        "error" => "failed",
                        _ => state
                    };
                    if (type == "task_complete" &&
                        payload.TryGetProperty("last_agent_message", out var lastAgentMessage))
                    {
                        var candidate = Sanitize(lastAgentMessage.GetString(), 150);
                        if (!string.IsNullOrWhiteSpace(candidate)) message = candidate;
                    }
                    else if (type == "error" && payload.TryGetProperty("message", out var errorMessage))
                    {
                        var candidate = Sanitize(errorMessage.GetString(), 150);
                        if (!string.IsNullOrWhiteSpace(candidate)) message = candidate;
                    }
                }
            }
            catch (JsonException) { }
        }

        // A fresh public progress message after the last lifecycle event means
        // this turn is active, even if task_started fell outside the tail window.
        if (state is not "review" and not "failed" &&
            !string.IsNullOrWhiteSpace(message) && updatedAt > lifecycleAt &&
            DateTimeOffset.Now - updatedAt < TimeSpan.FromMinutes(30))
            state = "running";

        return new SessionSnapshot(threadId, title, state, message, updatedAt);
    }

    private static bool IsSubagentSession(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
            var firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine)) return false;
            using var document = JsonDocument.Parse(firstLine);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var envelopeType) ||
                envelopeType.GetString() != "session_meta" ||
                !root.TryGetProperty("payload", out var payload)) return false;
            if (payload.TryGetProperty("thread_source", out var threadSource) &&
                threadSource.GetString()?.Equals("subagent", StringComparison.OrdinalIgnoreCase) == true)
                return true;
            return payload.TryGetProperty("source", out var source) &&
                   source.ValueKind == JsonValueKind.Object &&
                   source.TryGetProperty("subagent", out _);
        }
        catch (IOException) { return false; }
        catch (JsonException) { return false; }
    }

    private static string? ReadRecentUserTitle(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - TitleTailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 8192, leaveOpen: false);
            if (start > 0) reader.ReadLine();
            string? latest = null;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("payload", out var payload) ||
                        !payload.TryGetProperty("type", out var type) ||
                        type.GetString() != "user_message" ||
                        !payload.TryGetProperty("message", out var message)) continue;
                    var raw = message.GetString();
                    if (IsInternalContextMessage(raw)) continue;
                    var candidate = Sanitize(raw, 72);
                    if (!string.IsNullOrWhiteSpace(candidate)) latest = candidate;
                }
                catch (JsonException) { }
            }
            return latest;
        }
        catch (IOException) { return null; }
    }

    private void SettleToIdle()
    {
        var previous = _lastPublished;
        if (previous is null || previous.State != "review") return;
        Publish(previous with { State = "idle", Message = LocalizationService.T("Codex 已待命"), UpdatedAt = DateTimeOffset.Now });
    }

    private void Publish(CodexActivityState state)
    {
        var signature = $"{state.State}|{state.Message}|{state.UpdatedAt:O}|" +
                        string.Join(';', state.Tasks.Select(task => $"{task.ThreadId}:{task.State}:{task.UpdatedAt:O}:{task.Message}"));
        if (signature == _lastSignature) return;
        _lastSignature = signature;
        _lastPublished = state;
        _onChanged(state);
    }

    private static DateTimeOffset ReadTimestamp(JsonElement root, DateTime fallback)
    {
        if (root.TryGetProperty("timestamp", out var timestampElement) &&
            DateTimeOffset.TryParse(timestampElement.GetString(), out var parsed))
            return parsed;
        return new DateTimeOffset(fallback);
    }

    private static string ThreadIdFromName(string filename)
    {
        var match = Regex.Match(filename, @"([0-9a-f]{8}-[0-9a-f-]{27,})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(filename);
    }

    private static string StatusLabel(string state) => state switch
    {
        "running" => LocalizationService.T("运行中"),
        "waiting" => LocalizationService.T("需要输入"),
        "failed" => LocalizationService.T("已阻塞"),
        "review" => LocalizationService.T("已完成"),
        _ => LocalizationService.T("待机")
    };

    private static int StatePriority(string state) => state switch
    {
        "waiting" => 0,
        "failed" => 1,
        "running" => 2,
        "review" => 3,
        _ => 4
    };

    private static string Sanitize(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var value = Regex.Replace(text, @"!\[[^\]]*\]\([^\)]*\)", LocalizationService.T("[图片]"));
        value = Regex.Replace(value, @"\[([^\]]+)\]\([^\)]*\)", "$1");
        value = Regex.Replace(value, @"https?://\S+", LocalizationService.T("[链接]"));
        value = Regex.Replace(value, @"[A-Za-z]:\\[^\s，。；：]+", LocalizationService.T("[本地文件]"));
        value = value.Replace('`', ' ');
        value = Regex.Replace(value, @"\s+", " ").Trim();
        if (value.Length > maxLength)
            value = value[..maxLength].TrimEnd() + "…";
        return value;
    }

    private static bool IsInternalContextMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("<environment_context", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<app-context", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<codex_internal_context", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<recommended_plugins", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("# Chrome tabs:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultTaskTitle(string? title) =>
        string.Equals(title, "Codex 任务", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(title, LocalizationService.T("Codex 任务"), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
        _reviewTimer.Dispose();
        _pollTimer.Dispose();
    }

    private sealed record SessionSnapshot(
        string ThreadId,
        string Title,
        string State,
        string Message,
        DateTimeOffset UpdatedAt);

    private sealed record CachedSnapshot(long Length, DateTime LastWriteTimeUtc, SessionSnapshot Snapshot);
}
