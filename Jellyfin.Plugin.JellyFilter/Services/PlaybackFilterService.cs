using System.Collections.Concurrent;
using System.Globalization;
using Jellyfin.Plugin.JellyFilter.Configuration;
using Jellyfin.Plugin.JellyFilter.Data;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFilter.Services;

/// <summary>
/// Watches active playback sessions and acts on scenes the viewer has asked to filter.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin has no hook that fires when playback crosses a given timestamp, so the only way to
/// filter on arbitrary boundaries is to poll the reported position of each session and issue a
/// seek. Doing it server side rather than in the client is what makes per-user preferences
/// possible, and it works on clients that have no plugin support of their own.
/// </para>
/// <para>
/// Initializes a new instance of the <see cref="PlaybackFilterService"/> class.
/// </para>
/// </remarks>
/// <param name="sessionManager">Session manager.</param>
/// <param name="repository">Filter file repository.</param>
/// <param name="logger">Logger.</param>
public sealed class PlaybackFilterService(
    ISessionManager sessionManager,
    FilterFileRepository repository,
    ILogger<PlaybackFilterService> logger) : IHostedService, IDisposable
{
    /// <summary>
    /// How long to ignore a session after issuing a seek. Clients report their position on their
    /// own schedule, so without this the stale position would trigger the same seek repeatedly.
    /// </summary>
    private static readonly TimeSpan _seekSettleTime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How many times a single scene is seeked past before the plugin stops trying. A client that
    /// ignores seek commands would otherwise be told to jump forever.
    /// </summary>
    private const int MaxSkipAttempts = 3;

    private readonly ISessionManager _sessionManager = sessionManager;
    private readonly FilterFileRepository _repository = repository;
    private readonly ILogger<PlaybackFilterService> _logger = logger;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cancellation.Token), CancellationToken.None);
        _logger.LogInformation("JellyFilter playback watcher started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cancellation is null)
        {
            return;
        }

        await _cancellation.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown ran out of time; the loop observes its own token regardless.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var configuration = Plugin.Instance?.Configuration;
            var interval = Math.Clamp(configuration?.PollIntervalMs ?? 400, 100, 5000);

            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (configuration is null || !configuration.EnableAutoSkip)
            {
                continue;
            }

            try
            {
                await TickAsync(configuration, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable CA1031 // One misbehaving session must not take down the watcher.
            catch (Exception ex)
            {
                _logger.LogError(ex, "JellyFilter playback watcher tick failed");
            }
#pragma warning restore CA1031
        }
    }

    private async Task TickAsync(PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        var live = new HashSet<string>(StringComparer.Ordinal);

        foreach (var session in _sessionManager.Sessions)
        {
            if (session.NowPlayingItem is null)
            {
                // Playback ended. If it ended inside a muted scene the client is still muted,
                // and nothing else is going to undo that.
                if (_sessions.TryRemove(session.Id, out var finished))
                {
                    await ClearMuteAsync(session, finished, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            live.Add(session.Id);
            await ProcessSessionAsync(session, configuration, cancellationToken).ConfigureAwait(false);
        }

        // Forget sessions that have gone away entirely so a later playback starts from a clean slate.
        foreach (var id in _sessions.Keys)
        {
            if (!live.Contains(id))
            {
                _sessions.TryRemove(id, out _);
            }
        }
    }

    private async Task ProcessSessionAsync(
        SessionInfo session,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var state = _sessions.GetOrAdd(session.Id, _ => new SessionState());
        var itemId = session.NowPlayingItem.Id;
        if (state.ItemId != itemId)
        {
            // A mute belongs to the scene that caused it. Carrying it into the next item would
            // leave the client silent over content it was never meant to cover.
            await ClearMuteAsync(session, state, cancellationToken).ConfigureAwait(false);
            state.Reset(itemId);
        }

        var preferences = configuration.FindUser(session.UserId);
        if (preferences is null || !preferences.Enabled)
        {
            await ClearMuteAsync(session, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = session.FullNowPlayingItem;
        var filter = _repository.GetForMediaPath(item?.Path);
        if (!filter.HasScenes)
        {
            await ClearMuteAsync(session, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        var playState = session.PlayState;
        if (playState is null || playState.PositionTicks is null)
        {
            return;
        }

        if (DateTime.UtcNow < state.IgnoreUntil)
        {
            return;
        }

        var position = (double)playState.PositionTicks.Value / TimeSpan.TicksPerSecond;
        var scene = FindScene(filter, preferences, configuration, position, out var action);

        if (scene is null || action == FilterAction.Allow)
        {
            await ClearMuteAsync(session, state, cancellationToken).ConfigureAwait(false);
            state.ActiveSkipSceneKey = null;
            return;
        }

        if (action == FilterAction.Mute)
        {
            await ApplyMuteAsync(session, state, scene, playState.IsMuted, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Leaving the client muted while seeking away would strand it in that state.
        await ClearMuteAsync(session, state, cancellationToken).ConfigureAwait(false);

        if (playState.IsPaused)
        {
            return;
        }

        if (!string.Equals(state.ActiveSkipSceneKey, scene.Key, StringComparison.Ordinal))
        {
            state.ActiveSkipSceneKey = scene.Key;
            state.SkipAttempts = 0;
        }

        if (state.SkipAttempts >= MaxSkipAttempts)
        {
            return;
        }

        state.SkipAttempts++;
        if (state.SkipAttempts == MaxSkipAttempts)
        {
            _logger.LogWarning(
                "Client {Client} did not honour the seek past {Category} at {Start}s in {Item}; giving up on this scene",
                session.Client,
                scene.Category,
                scene.Start,
                session.NowPlayingItem.Name);
        }

        var target = ResolveSkipTarget(filter, preferences, configuration, scene);
        state.IgnoreUntil = DateTime.UtcNow + _seekSettleTime;

        await SeekAsync(session, scene, target, configuration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the scene covering the current position that the user wants acted on.
    /// </summary>
    internal static FilterScene? FindScene(
        MediaFilter filter,
        UserFilterPreferences preferences,
        PluginConfiguration configuration,
        double position,
        out FilterAction action)
    {
        FilterScene? muteCandidate = null;

        foreach (var scene in filter.Scenes)
        {
            if (scene.Start > position)
            {
                // Scenes are sorted by start time, so nothing further can contain the position.
                break;
            }

            if (!scene.Contains(position) || scene.DurationSeconds < configuration.MinimumSceneSeconds)
            {
                continue;
            }

            var resolved = preferences.ResolveAction(scene);
            if (resolved == FilterAction.Skip)
            {
                // Skipping supersedes muting when scenes overlap: it removes the content outright.
                action = FilterAction.Skip;
                return scene;
            }

            if (resolved == FilterAction.Mute && muteCandidate is null)
            {
                muteCandidate = scene;
            }
        }

        action = muteCandidate is null ? FilterAction.Allow : FilterAction.Mute;
        return muteCandidate;
    }

    /// <summary>
    /// Works out where playback should resume, absorbing any adjacent scenes the user also wants
    /// skipped so that a run of filtered content costs a single seek.
    /// </summary>
    internal static double ResolveSkipTarget(
        MediaFilter filter,
        UserFilterPreferences preferences,
        PluginConfiguration configuration,
        FilterScene scene)
    {
        var end = scene.End;
        bool extended;

        do
        {
            extended = false;
            foreach (var candidate in filter.Scenes)
            {
                if (candidate.Start > end)
                {
                    break;
                }

                if (candidate.End <= end || candidate.DurationSeconds < configuration.MinimumSceneSeconds)
                {
                    continue;
                }

                if (preferences.ResolveAction(candidate) == FilterAction.Skip)
                {
                    end = candidate.End;
                    extended = true;
                }
            }
        }
        while (extended);

        return end + Math.Max(0, configuration.SkipPaddingSeconds);
    }

    private async Task SeekAsync(
        SessionInfo session,
        FilterScene scene,
        double targetSeconds,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.ShowSkipNotification)
        {
            var text = configuration.SkipNotificationFormat.Replace(
                "{category}",
                scene.Category,
                StringComparison.OrdinalIgnoreCase);

            await _sessionManager.SendMessageCommand(
                session.Id,
                session.Id,
                new MessageCommand
                {
                    // Some clients reject a null header.
                    Header = string.Empty,
                    Text = text,
                    TimeoutMs = 2000,
                },
                cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug(
            "Skipping {Category} ({Start}s to {End}s) for {User} on {Client}",
            scene.Category,
            scene.Start.ToString("F1", CultureInfo.InvariantCulture),
            scene.End.ToString("F1", CultureInfo.InvariantCulture),
            session.UserName,
            session.Client);

        await _sessionManager.SendPlaystateCommand(
            session.Id,
            session.Id,
            new PlaystateRequest
            {
                Command = PlaystateCommand.Seek,
                ControllingUserId = session.UserId.ToString("N"),
                SeekPositionTicks = (long)(targetSeconds * TimeSpan.TicksPerSecond),
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyMuteAsync(
        SessionInfo session,
        SessionState state,
        FilterScene scene,
        bool alreadyMuted,
        CancellationToken cancellationToken)
    {
        if (string.Equals(state.MutedForSceneKey, scene.Key, StringComparison.Ordinal))
        {
            return;
        }

        state.MutedForSceneKey = scene.Key;

        // Someone who muted their own client should not have the sound turned back on for them
        // when the scene ends, so only take responsibility for a mute this plugin caused.
        state.MutedByPlugin = !alreadyMuted;
        if (alreadyMuted)
        {
            return;
        }

        _logger.LogDebug(
            "Muting {Category} ({Start}s to {End}s) for {User}",
            scene.Category,
            scene.Start.ToString("F1", CultureInfo.InvariantCulture),
            scene.End.ToString("F1", CultureInfo.InvariantCulture),
            session.UserName);

        await SendGeneralCommandAsync(session, GeneralCommandType.Mute, cancellationToken).ConfigureAwait(false);
    }

    private async Task ClearMuteAsync(SessionInfo session, SessionState state, CancellationToken cancellationToken)
    {
        if (state.MutedForSceneKey is null)
        {
            return;
        }

        state.MutedForSceneKey = null;
        if (!state.MutedByPlugin)
        {
            return;
        }

        state.MutedByPlugin = false;
        await SendGeneralCommandAsync(session, GeneralCommandType.Unmute, cancellationToken).ConfigureAwait(false);
    }

    private Task SendGeneralCommandAsync(
        SessionInfo session,
        GeneralCommandType command,
        CancellationToken cancellationToken)
    {
        return _sessionManager.SendGeneralCommand(
            session.Id,
            session.Id,
            new GeneralCommand
            {
                Name = command,
                ControllingUserId = session.UserId,
            },
            cancellationToken);
    }

    private sealed class SessionState
    {
        public Guid ItemId { get; private set; }

        public string? ActiveSkipSceneKey { get; set; }

        public string? MutedForSceneKey { get; set; }

        public bool MutedByPlugin { get; set; }

        public int SkipAttempts { get; set; }

        public DateTime IgnoreUntil { get; set; }

        public void Reset(Guid itemId)
        {
            ItemId = itemId;
            ActiveSkipSceneKey = null;
            MutedForSceneKey = null;
            MutedByPlugin = false;
            SkipAttempts = 0;
            IgnoreUntil = DateTime.MinValue;
        }
    }
}
