using System.IO;
using Windows.Media.Control;

namespace GuguPet;

public sealed record MusicPlaybackState(
    bool Available,
    bool IsPlaying,
    string Title,
    string Artist,
    string Source,
    bool CanToggle,
    bool CanPrevious,
    bool CanNext,
    string? Error = null)
{
    public static MusicPlaybackState Unavailable(string? error = null) =>
        new(false, false, "", "", "", false, false, false, error);
}

/// <summary>
/// Event-driven bridge to Windows Global System Media Transport Controls.
/// It reads only the playback state and public media metadata exposed by the
/// active player; it never captures or analyses audio data.
/// </summary>
public sealed class MusicPlaybackService : IDisposable
{
    private readonly Action<MusicPlaybackState> _onStateChanged;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _enabled;
    private bool _disposed;

    public MusicPlaybackService(Action<MusicPlaybackState> onStateChanged) =>
        _onStateChanged = onStateChanged;

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_disposed) return;
        _enabled = enabled;
        if (!enabled)
        {
            DetachSession();
            DetachManager();
            Publish(MusicPlaybackState.Unavailable());
            return;
        }

        if (_manager is null)
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += Manager_OnCurrentSessionChanged;
                _manager.SessionsChanged += Manager_OnSessionsChanged;
            }
            catch (Exception ex)
            {
                Publish(MusicPlaybackState.Unavailable(ex.Message));
                return;
            }
        }

        await AttachBestSessionAsync();
    }

    public async Task<bool> TogglePlayPauseAsync()
    {
        var session = _session;
        if (!_enabled || session is null) return false;
        try { return await session.TryTogglePlayPauseAsync(); }
        catch { return false; }
    }

    public async Task<bool> PreviousAsync()
    {
        var session = _session;
        if (!_enabled || session is null) return false;
        try { return await session.TrySkipPreviousAsync(); }
        catch { return false; }
    }

    public async Task<bool> NextAsync()
    {
        var session = _session;
        if (!_enabled || session is null) return false;
        try { return await session.TrySkipNextAsync(); }
        catch { return false; }
    }

    private async void Manager_OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => await AttachBestSessionAsync();

    private async void Manager_OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => await AttachBestSessionAsync();

    private async Task AttachBestSessionAsync()
    {
        if (!_enabled || _manager is null || _disposed) return;
        try
        {
            var current = _manager.GetCurrentSession();
            var selected = IsPlaying(current)
                ? current
                : _manager.GetSessions().FirstOrDefault(IsPlaying) ?? current;
            if (!ReferenceEquals(selected, _session))
            {
                DetachSession();
                _session = selected;
                if (_session is not null)
                {
                    _session.PlaybackInfoChanged += Session_OnPlaybackInfoChanged;
                    _session.MediaPropertiesChanged += Session_OnMediaPropertiesChanged;
                }
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            if (_enabled) Publish(MusicPlaybackState.Unavailable(ex.Message));
        }
    }

    private async void Session_OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        if (!IsPlaying(sender) && _manager is not null &&
            _manager.GetSessions().Any(candidate => !ReferenceEquals(candidate, sender) && IsPlaying(candidate)))
            await AttachBestSessionAsync();
        else
            await RefreshAsync();
    }

    private async void Session_OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (!_enabled || _disposed) return;
        await _refreshGate.WaitAsync();
        try
        {
            var session = _session;
            if (session is null)
            {
                Publish(MusicPlaybackState.Unavailable());
                return;
            }

            var playback = session.GetPlaybackInfo();
            var controls = playback.Controls;
            var properties = await session.TryGetMediaPropertiesAsync();
            var state = new MusicPlaybackState(
                true,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                properties?.Title ?? "",
                properties?.Artist ?? "",
                FriendlySource(session.SourceAppUserModelId),
                controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled);
            if (_enabled && ReferenceEquals(session, _session)) Publish(state);
        }
        catch (Exception ex)
        {
            if (_enabled) Publish(MusicPlaybackState.Unavailable(ex.Message));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null) return false;
        try
        {
            return session.GetPlaybackInfo().PlaybackStatus ==
                   GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { return false; }
    }

    private static string FriendlySource(string sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId)) return "";
        var source = sourceAppId.Replace('!', '.');
        var last = source.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? source;
        return last.Equals("exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(sourceAppId)
            : last;
    }

    private void Publish(MusicPlaybackState state)
    {
        if (!_disposed) _onStateChanged(state);
    }

    private void DetachSession()
    {
        if (_session is null) return;
        _session.PlaybackInfoChanged -= Session_OnPlaybackInfoChanged;
        _session.MediaPropertiesChanged -= Session_OnMediaPropertiesChanged;
        _session = null;
    }

    private void DetachManager()
    {
        if (_manager is null) return;
        _manager.CurrentSessionChanged -= Manager_OnCurrentSessionChanged;
        _manager.SessionsChanged -= Manager_OnSessionsChanged;
        _manager = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _enabled = false;
        DetachSession();
        DetachManager();
        _disposed = true;
    }
}
