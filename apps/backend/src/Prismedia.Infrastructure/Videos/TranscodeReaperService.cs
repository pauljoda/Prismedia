using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;
using Prismedia.Application.Videos;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Periodically reaps transcode and remux ffmpeg jobs whose playback session has gone silent — a
/// closed tab, a crashed client, or a long pause — or that have run past a hard lifetime ceiling, so
/// abandoned encodings cannot accumulate and saturate the host. Liveness is the heartbeat the player
/// already sends (<see cref="ITranscodeSessionService.Ping"/>); cancelling a job tears down its
/// ffmpeg process tree while leaving already-produced segments in the cache, so a reaped session
/// resumes from cache and only re-encodes from the frontier. This is the single, universal cleanup
/// path: every transcode and remux is keyed by item id, so one sweep covers them all.
/// <para>
/// The same sweep also enforces the configured transcode-cache size limit: cancelling a job leaves
/// its segments on disk, so without a ceiling the cache would grow indefinitely as more videos are
/// watched. After reaping, the least-recently-played cached items (that are not currently playing)
/// are evicted until the cache is back under the limit.
/// </para>
/// </summary>
public sealed class TranscodeReaperService : BackgroundService {
    /// <summary>How often the reaper sweeps for abandoned jobs.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A session counts as live while it has pinged within this window, and is dropped once it goes
    /// older. The player pings roughly every ten seconds while playing, so this tolerates a dozen
    /// missed heartbeats (brief stalls, pauses) before treating the viewer as gone.
    /// </summary>
    private static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(120);

    /// <summary>Jobs younger than this are never reaped, so startup and brief gaps survive a sweep.</summary>
    private static readonly TimeSpan IdleGrace = TimeSpan.FromSeconds(60);

    /// <summary>Absolute ceiling: no single encoding outlives this, even if its session looks live.</summary>
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(6);

    private readonly ITranscodeSessionService _sessions;
    private readonly ITranscodeCacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TranscodeReaperService> _logger;

    /// <summary>Creates the reaper over the shared transcode session registry and cache manager.</summary>
    public TranscodeReaperService(
        ITranscodeSessionService sessions,
        ITranscodeCacheService cache,
        IServiceScopeFactory scopeFactory,
        ILogger<TranscodeReaperService> logger) {
        _sessions = sessions;
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(SweepInterval);
        try {
            while (await timer.WaitForNextTickAsync(stoppingToken)) {
                try {
                    await SweepAsync(stoppingToken);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Transcode reaper sweep failed.");
                }
            }
        } catch (OperationCanceledException) {
            // Host is shutting down; nothing to clean up here.
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken) {
        var liveItemIds = _sessions.LiveItemIds(SessionTtl);
        var staleSessions = _sessions.ReapStaleSessions(SessionTtl);
        var reapedJobs = HlsAssetService.ReapOrphanedJobs(liveItemIds, IdleGrace, MaxLifetime);
        if (staleSessions > 0 || reapedJobs > 0) {
            _logger.LogInformation(
                "Transcode reaper cleared {StaleSessions} abandoned session(s) and cancelled {ReapedJobs} orphaned or expired ffmpeg job(s).",
                staleSessions,
                reapedJobs);
        }

        var maxBytes = await ResolveCacheLimitBytesAsync(cancellationToken);
        if (maxBytes > 0) {
            _cache.PruneToLimit(maxBytes, liveItemIds);
        }
    }

    // Reads the configured maximum cache size (in bytes) from settings; 0 means unlimited.
    private async Task<long> ResolveCacheLimitBytesAsync(CancellationToken cancellationToken) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var hls = await settings.GetHlsSettingsAsync(cancellationToken);
            return ITranscodeCacheService.GigabytesToBytes(hls.MaxCacheSizeGb);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Could not read the transcode cache limit; skipping eviction this sweep.");
            return 0;
        }
    }
}
