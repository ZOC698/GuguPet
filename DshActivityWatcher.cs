using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuguPet;

/// <summary>
/// Observes DeepSeek Harness through its public loopback RPC/event transport.
/// While connected this is fully event-driven: session.list supplies the
/// baseline, then the host and mux WebSockets carry all activity changes.
/// </summary>
public sealed class DshActivityWatcher : IDisposable
{
    private static readonly TimeSpan ReviewDisplayDuration = TimeSpan.FromSeconds(8);
    private static readonly int[] FallbackPorts = { 5556, 3080 };
    private const int MaxTasks = 8;
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly object _gate = new();
    private readonly Action<CodexActivityState> _onChanged;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Threading.Timer _reviewTimer;
    private readonly Dictionary<string, DshSession> _sessions = new(StringComparer.Ordinal);
    private readonly Task _runner;
    private string? _lastSignature;
    private bool _connected;

    public DshActivityWatcher(Action<CodexActivityState> onChanged)
    {
        _onChanged = onChanged;
        _reviewTimer = new System.Threading.Timer(_ => SettleReviews(), null, Timeout.Infinite, Timeout.Infinite);
        _runner = Task.Run(() => RunAsync(_shutdown.Token));
    }

    private async Task RunAsync(CancellationToken shutdown)
    {
        var retryDelay = TimeSpan.FromSeconds(2);
        while (!shutdown.IsCancellationRequested)
        {
            using var generation = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            try
            {
                var endpoint = await FindEndpointAndRefreshAsync(generation.Token);
                using var hostSocket = new ClientWebSocket();
                using var muxSocket = new ClientWebSocket();
                await Task.WhenAll(
                    hostSocket.ConnectAsync(WebSocketUri(endpoint, "api/events.host"), generation.Token),
                    muxSocket.ConnectAsync(WebSocketUri(endpoint, "api/events.mux"), generation.Token));

                lock (_gate) _connected = true;
                Publish();
                retryDelay = TimeSpan.FromSeconds(2);

                var hostLoop = ReceiveLoopAsync(hostSocket, HandleHostEnvelope, generation.Token);
                var muxLoop = ReceiveLoopAsync(muxSocket, HandleMuxEnvelope, generation.Token);
                await Task.WhenAny(hostLoop, muxLoop);
                generation.Cancel();
                await IgnoreCancellationAsync(hostLoop);
                await IgnoreCancellationAsync(muxLoop);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            catch (HttpRequestException) { }
            catch (WebSocketException) { }
            catch (IOException) { }
            catch (JsonException) { }
            catch (ObjectDisposedException) when (shutdown.IsCancellationRequested) { }
            finally
            {
                lock (_gate)
                {
                    _connected = false;
                    foreach (var session in _sessions.Values)
                    {
                        session.Running = false;
                        session.PendingInteractions.Clear();
                    }
                }
                Publish();
            }

            if (shutdown.IsCancellationRequested) break;
            try { await Task.Delay(retryDelay, shutdown); }
            catch (OperationCanceledException) { break; }
            retryDelay = TimeSpan.FromSeconds(Math.Min(30, retryDelay.TotalSeconds * 1.8));
        }
    }

    private async Task<Uri> FindEndpointAndRefreshAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var endpoint in CandidateEndpoints())
        {
            try
            {
                await RefreshBaselineAsync(new Uri(endpoint, "api/session.list"), cancellationToken);
                return endpoint;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
            {
                lastError = error;
            }
        }
        throw new HttpRequestException("No local DSH endpoint is available.", lastError);
    }

    private static IEnumerable<Uri> CandidateEndpoints()
    {
        var ports = new HashSet<int>();
        foreach (var port in DiscoverDshBackendPorts().Concat(FallbackPorts))
        {
            if (port is <= 0 or > 65535 || !ports.Add(port)) continue;
            yield return new Uri($"http://127.0.0.1:{port}/");
        }
    }

    private static IEnumerable<int> DiscoverDshBackendPorts()
    {
        if (!OperatingSystem.IsWindows()) yield break;

        object? locator = null;
        object? services = null;
        object? results = null;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null) yield break;
            locator = Activator.CreateInstance(locatorType);
            if (locator is null) yield break;
            services = locatorType.InvokeMember(
                "ConnectServer",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                locator,
                new object?[] { ".", "root\\cimv2" });
            if (services is null) yield break;
            results = services.GetType().InvokeMember(
                "ExecQuery",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                services,
                new object?[] { "SELECT CommandLine FROM Win32_Process WHERE Name = 'node.exe'" });
            if (results is not System.Collections.IEnumerable processes) yield break;

            foreach (var process in processes)
            {
                if (process is null) continue;
                try
                {
                    var commandLine = process.GetType().InvokeMember(
                        "CommandLine",
                        System.Reflection.BindingFlags.GetProperty,
                        null,
                        process,
                        null) as string;
                    if (string.IsNullOrWhiteSpace(commandLine) ||
                        !commandLine.Contains("@deepseek-ai", StringComparison.OrdinalIgnoreCase) ||
                        !commandLine.Contains("dsh", StringComparison.OrdinalIgnoreCase) ||
                        !Regex.IsMatch(commandLine, @"(?:^|\s)web(?:\s|$)", RegexOptions.IgnoreCase))
                        continue;

                    var match = Regex.Match(
                        commandLine,
                        @"(?:^|\s)--port(?:=|\s+)(?<port>\d{1,5})(?:\s|$)",
                        RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups["port"].Value, out var port))
                        yield return port;
                }
                finally
                {
                    if (Marshal.IsComObject(process)) Marshal.FinalReleaseComObject(process);
                }
            }
        }
        finally
        {
            if (results is not null && Marshal.IsComObject(results)) Marshal.FinalReleaseComObject(results);
            if (services is not null && Marshal.IsComObject(services)) Marshal.FinalReleaseComObject(services);
            if (locator is not null && Marshal.IsComObject(locator)) Marshal.FinalReleaseComObject(locator);
        }
    }

    private async Task RefreshBaselineAsync(Uri sessionListUri, CancellationToken cancellationToken)
    {
        var rpcId = Guid.NewGuid().ToString();
        using var response = await _http.PostAsJsonAsync(sessionListUri, new
        {
            type = "client-request",
            rpcId,
            method = "session.list",
            payload = new { }
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("ok", out var ok) || !ok.GetBoolean() ||
            !result.TryGetProperty("value", out var value) ||
            !value.TryGetProperty("items", out var items))
            throw new JsonException("Invalid DSH session.list response.");

        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("sessionId", out var idElement)) continue;
                var id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                var subagent = item.TryGetProperty("origin", out var originElement) &&
                               originElement.GetString() == "subagent";
                if (subagent) continue;

                seen.Add(id);
                if (!_sessions.TryGetValue(id, out var session))
                    _sessions[id] = session = new DshSession(id);
                session.Blank = item.TryGetProperty("blank", out var blankElement) && blankElement.GetBoolean();
                session.Running = item.TryGetProperty("running", out var runningElement) && runningElement.GetBoolean();
                session.UpdatedAt = ReadUnixTime(item, "updatedAt", session.UpdatedAt);
                session.Title = ReadProjectionTitle(item) ?? session.Title;
                session.PendingInteractions.Clear();
                session.Failure = null;
                session.ReviewUntil = null;
                if (session.Running)
                {
                    session.Blank = false;
                }
            }

            foreach (var id in _sessions.Keys.Where(id => !seen.Contains(id)).ToArray())
                _sessions.Remove(id);
        }
    }

    private static Uri WebSocketUri(Uri endpoint, string path)
    {
        var builder = new UriBuilder(new Uri(endpoint, path))
        {
            Scheme = endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws"
        };
        return builder.Uri;
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        Action<JsonElement> handle,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (message.Length + result.Count > MaxMessageBytes)
                    throw new IOException("DSH event frame exceeded the safety limit.");
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;
            using var document = JsonDocument.Parse(message.ToArray());
            handle(document.RootElement);
        }
    }

    private void HandleHostEnvelope(JsonElement envelope)
    {
        if (!TryGetPayload(envelope, out var payload)) return;
        var type = payload.GetProperty("type").GetString();
        lock (_gate)
        {
            switch (type)
            {
                case "host/session-added":
                {
                    if (!TryGetTopLevelSession(payload, out var session)) break;
                    session.Blank = payload.TryGetProperty("blank", out var blank) && blank.GetBoolean();
                    session.UpdatedAt = DateTimeOffset.Now;
                    break;
                }
                case "host/session-removed":
                {
                    if (!TryGetSession(payload, out var session)) break;
                    var wasRunning = session.Running;
                    session.Running = false;
                    session.PendingInteractions.Clear();
                    if (wasRunning && session.ReviewUntil is null)
                        session.ReviewUntil = DateTimeOffset.Now + ReviewDisplayDuration;
                    session.UpdatedAt = DateTimeOffset.Now;
                    break;
                }
                case "host/session-status":
                {
                    if (!TryGetSession(payload, out var session)) break;
                    var running = payload.TryGetProperty("running", out var runningElement) && runningElement.GetBoolean();
                    var wasRunning = session.Running;
                    session.Running = running;
                    session.UpdatedAt = DateTimeOffset.Now;
                    if (running)
                    {
                        session.Failure = null;
                        session.ReviewUntil = null;
                    }
                    else if (wasRunning && session.PendingInteractions.Count == 0)
                    {
                        session.ReviewUntil = DateTimeOffset.Now + ReviewDisplayDuration;
                    }
                    break;
                }
                case "host/agent-error":
                {
                    if (!TryGetSession(payload, out var session)) break;
                    session.Running = false;
                    session.ReviewUntil = null;
                    session.Failure = payload.TryGetProperty("message", out var message)
                        ? Sanitize(message.GetString(), 150)
                        : LocalizationService.T("DSH 任务出现错误");
                    session.UpdatedAt = DateTimeOffset.Now;
                    break;
                }
            }
        }
        ScheduleReviewTimer();
        Publish();
    }

    private void HandleMuxEnvelope(JsonElement envelope)
    {
        if (!TryGetPayload(envelope, out var payload)) return;
        var type = payload.GetProperty("type").GetString();
        var rpcId = envelope.TryGetProperty("rpcId", out var rpcElement) ? rpcElement.GetString() : null;
        lock (_gate)
        {
            switch (type)
            {
                case "approval/requested":
                    if (TryGetSession(payload, out var approvalSession))
                    {
                        var key = payload.TryGetProperty("approvalId", out var approvalId)
                            ? $"approval:{approvalId.GetString()}"
                            : $"approval:{rpcId}";
                        approvalSession.PendingInteractions.Add(key);
                        approvalSession.UpdatedAt = DateTimeOffset.Now;
                    }
                    break;
                case "approval/resolved":
                    if (TryGetSession(payload, out var resolvedApproval))
                    {
                        var key = payload.TryGetProperty("approvalId", out var approvalId)
                            ? $"approval:{approvalId.GetString()}"
                            : $"approval:{rpcId}";
                        resolvedApproval.PendingInteractions.Remove(key);
                        resolvedApproval.UpdatedAt = DateTimeOffset.Now;
                    }
                    break;
                case "question/requested":
                    if (TryGetSession(payload, out var questionSession))
                    {
                        questionSession.PendingInteractions.Add($"question:{rpcId}");
                        questionSession.UpdatedAt = DateTimeOffset.Now;
                    }
                    break;
                case "question/resolved":
                    if (TryGetSession(payload, out var resolvedQuestion))
                    {
                        var questionId = payload.TryGetProperty("questionRpcId", out var questionRpcId)
                            ? questionRpcId.GetString()
                            : rpcId;
                        resolvedQuestion.PendingInteractions.Remove($"question:{questionId}");
                        resolvedQuestion.UpdatedAt = DateTimeOffset.Now;
                    }
                    break;
                case "session/projection":
                    if (TryGetSession(payload, out var projectedSession) &&
                        payload.TryGetProperty("key", out var keyElement) && keyElement.GetString() == "title" &&
                        payload.TryGetProperty("value", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
                    {
                        projectedSession.Title = Sanitize(titleElement.GetString(), 72);
                        projectedSession.UpdatedAt = DateTimeOffset.Now;
                    }
                    break;
                case "session/event":
                    HandleSessionEvent(payload);
                    break;
            }
        }
        ScheduleReviewTimer();
        Publish();
    }

    private void HandleSessionEvent(JsonElement payload)
    {
        if (!TryGetSession(payload, out var session) ||
            !payload.TryGetProperty("event", out var eventElement) ||
            !eventElement.TryGetProperty("type", out var typeElement)) return;

        var eventType = typeElement.GetString();
        session.UpdatedAt = ReadUnixTime(eventElement, "time", DateTimeOffset.Now);
        switch (eventType)
        {
            case "turn/start":
                session.Blank = false;
                session.Running = true;
                session.Failure = null;
                session.ReviewUntil = null;
                break;
            case "user/message":
                if (eventElement.TryGetProperty("data", out var userData) &&
                    userData.TryGetProperty("source", out var source) &&
                    source.TryGetProperty("kind", out var sourceKind) && sourceKind.GetString() == "user")
                    session.Blank = false;
                break;
            case "assistant/message":
                if (eventElement.TryGetProperty("data", out var assistantData))
                {
                    var text = ExtractAssistantText(assistantData);
                    if (!string.IsNullOrWhiteSpace(text)) session.Message = Sanitize(text, 150);
                }
                break;
            case "turn/end":
                session.Running = false;
                JsonElement reason = default;
                var hasReason = eventElement.TryGetProperty("data", out var endData) &&
                                endData.TryGetProperty("reason", out reason);
                var kind = hasReason && reason.TryGetProperty("kind", out var kindElement)
                    ? kindElement.GetString()
                    : null;
                if (kind is "error" or "blocked" or "interrupted")
                {
                    session.Failure = ReadTurnFailure(reason) ?? LocalizationService.T("DSH 任务出现错误");
                    session.ReviewUntil = null;
                }
                else if (kind == "aborted")
                {
                    session.Failure = null;
                    session.ReviewUntil = null;
                }
                else
                {
                    session.Failure = null;
                    session.ReviewUntil = DateTimeOffset.Now + ReviewDisplayDuration;
                }
                break;
        }
    }

    private bool TryGetTopLevelSession(JsonElement payload, out DshSession session)
    {
        session = null!;
        if (payload.TryGetProperty("origin", out var origin) && origin.GetString() == "subagent") return false;
        if (!payload.TryGetProperty("sessionId", out var idElement)) return false;
        var id = idElement.GetString();
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (!_sessions.TryGetValue(id, out session!))
            _sessions[id] = session = new DshSession(id);
        return true;
    }

    private bool TryGetSession(JsonElement payload, out DshSession session)
    {
        session = null!;
        if (!payload.TryGetProperty("sessionId", out var idElement)) return false;
        var id = idElement.GetString();
        return !string.IsNullOrWhiteSpace(id) && _sessions.TryGetValue(id, out session!);
    }

    private static bool TryGetPayload(JsonElement envelope, out JsonElement payload)
    {
        payload = default;
        return envelope.ValueKind == JsonValueKind.Object &&
               envelope.TryGetProperty("type", out var envelopeType) &&
               envelopeType.GetString() == "server-request" &&
               envelope.TryGetProperty("payload", out payload) &&
               payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty("type", out _);
    }

    private void Publish()
    {
        CodexActivityState state;
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            var tasks = _sessions.Values
                .Where(session => !session.Blank)
                .Select(session => ToTask(session, now))
                .OrderBy(task => StatePriority(task.State))
                .ThenByDescending(task => task.UpdatedAt)
                .Take(MaxTasks)
                .ToArray();
            var focus = tasks.FirstOrDefault();
            var sourceMessage = !_connected && tasks.Length == 0
                ? LocalizationService.T("DSH 未运行")
                : LocalizationService.T("DSH 已待命");
            state = focus is null
                ? new CodexActivityState("idle", sourceMessage, now, "", tasks, "dsh")
                : new CodexActivityState(focus.State, focus.Message, focus.UpdatedAt, focus.ThreadId, tasks, "dsh");

            var signature = $"{_connected}|{state.State}|{state.Message}|" +
                            string.Join(';', tasks.Select(task =>
                                $"{task.ThreadId}:{task.State}:{task.UpdatedAt:O}:{task.Title}:{task.Message}"));
            if (signature == _lastSignature) return;
            _lastSignature = signature;
        }
        _onChanged(state);
    }

    private static CodexTaskSummary ToTask(DshSession session, DateTimeOffset now)
    {
        string state;
        string message;
        if (session.PendingInteractions.Count > 0)
        {
            state = "waiting";
            message = LocalizationService.T("DSH 需要你的输入或批准");
        }
        else if (!string.IsNullOrWhiteSpace(session.Failure))
        {
            state = "failed";
            message = session.Failure;
        }
        else if (session.Running)
        {
            state = "running";
            message = string.IsNullOrWhiteSpace(session.Message)
                ? LocalizationService.T("DSH 正在处理任务")
                : session.Message;
        }
        else if (session.ReviewUntil is DateTimeOffset reviewUntil && reviewUntil > now)
        {
            state = "review";
            message = string.IsNullOrWhiteSpace(session.Message)
                ? LocalizationService.T("DSH 已完成任务")
                : session.Message;
        }
        else
        {
            state = "idle";
            message = LocalizationService.T("DSH 已待命");
        }

        return new CodexTaskSummary(
            session.Id,
            string.IsNullOrWhiteSpace(session.Title) ? LocalizationService.T("DSH 任务") : session.Title,
            state,
            StatusLabel(state),
            message,
            session.UpdatedAt,
            "dsh");
    }

    private void ScheduleReviewTimer()
    {
        lock (_gate)
        {
            var next = _sessions.Values
                .Where(session => session.ReviewUntil is not null)
                .Select(session => session.ReviewUntil!.Value)
                .DefaultIfEmpty()
                .Min();
            if (next == default)
            {
                _reviewTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }
            var due = next - DateTimeOffset.Now;
            _reviewTimer.Change(due <= TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }
    }

    private void SettleReviews()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            foreach (var session in _sessions.Values)
                if (session.ReviewUntil <= now) session.ReviewUntil = null;
        }
        ScheduleReviewTimer();
        Publish();
    }

    private static string? ReadProjectionTitle(JsonElement item)
    {
        if (!item.TryGetProperty("projections", out var projections) ||
            !projections.TryGetProperty("values", out var values) ||
            !values.TryGetProperty("title", out var title) ||
            title.ValueKind != JsonValueKind.String) return null;
        return Sanitize(title.GetString(), 72);
    }

    private static DateTimeOffset ReadUnixTime(JsonElement element, string property, DateTimeOffset fallback)
    {
        if (!element.TryGetProperty(property, out var value) || !value.TryGetInt64(out var milliseconds))
            return fallback;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
        catch (ArgumentOutOfRangeException) { return fallback; }
    }

    private static string? ExtractAssistantText(JsonElement data)
    {
        if (!data.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array) return null;
        return string.Join(" ", content.EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Select(block => block.TryGetProperty("text", out var text) ? text.GetString() : null)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string? ReadTurnFailure(JsonElement reason)
    {
        if (!reason.TryGetProperty("error", out var error)) return null;
        return error.TryGetProperty("message", out var message) ? Sanitize(message.GetString(), 150) : null;
    }

    private static string Sanitize(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var value = Regex.Replace(text, @"!\[[^\]]*\]\([^\)]*\)", LocalizationService.T("[图片]"));
        value = Regex.Replace(value, @"\[([^\]]+)\]\([^\)]*\)", "$1");
        value = Regex.Replace(value, @"https?://\S+", LocalizationService.T("[链接]"));
        value = Regex.Replace(value, @"[A-Za-z]:\\[^\s，。；：]+", LocalizationService.T("[本地文件]"));
        value = value.Replace('`', ' ');
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "…";
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

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (IOException) { }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _runner.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _reviewTimer.Dispose();
        _http.Dispose();
        _shutdown.Dispose();
    }

    private sealed class DshSession
    {
        public DshSession(string id) => Id = id;
        public string Id { get; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Failure { get; set; }
        public bool Running { get; set; }
        public bool Blank { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset? ReviewUntil { get; set; }
        public HashSet<string> PendingInteractions { get; } = new(StringComparer.Ordinal);
    }
}
