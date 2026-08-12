using System.Text.Json;
using System.IO;

namespace GuguPet;

public sealed record BridgeState(string State, string Message, DateTimeOffset UpdatedAt)
{
    public static BridgeState Idle { get; } = new("idle", LocalizationService.T("等待状态更新"), DateTimeOffset.Now);
}

public sealed class BridgeStateWatcher : IDisposable
{
    private readonly string _path;
    private readonly Action<BridgeState> _onChanged;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounce;

    public BridgeStateWatcher(string path, Action<BridgeState> onChanged)
    {
        _path = path;
        _onChanged = onChanged;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if (!File.Exists(path))
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                state = "idle",
                message = LocalizationService.T("独立运行模式")
            }));

        _debounce = new System.Threading.Timer(_ => ReadState(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => QueueRead();
        _watcher.Created += (_, _) => QueueRead();
        _watcher.Renamed += (_, _) => QueueRead();
        ReadState();
    }

    private void QueueRead() => _debounce.Change(80, Timeout.Infinite);

    private void ReadState()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var state = root.TryGetProperty("state", out var stateValue) ? stateValue.GetString() : "idle";
            var message = root.TryGetProperty("message", out var messageValue) ? messageValue.GetString() : "";
            if (!AnimationCatalog.IsValidState(state))
                return;
            _onChanged(new BridgeState(state!, message ?? "", DateTimeOffset.Now));
        }
        catch (IOException) { QueueRead(); }
        catch (JsonException) { }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
