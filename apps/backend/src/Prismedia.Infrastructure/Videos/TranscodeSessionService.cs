using System.Collections.Concurrent;
using Prismedia.Application.Videos;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// In-memory transcode session registry. Process ownership hooks will attach here as the HLS manager deepens.
/// </summary>
public sealed class TranscodeSessionService : ITranscodeSessionService {
    private readonly ConcurrentDictionary<string, ActiveTranscodeSession> _sessions = new(StringComparer.Ordinal);

    public void Register(string playSessionId, Guid itemId) {
        if (string.IsNullOrWhiteSpace(playSessionId)) {
            return;
        }

        _sessions.AddOrUpdate(
            playSessionId,
            _ => new ActiveTranscodeSession(itemId, DateTimeOffset.UtcNow),
            (_, existing) => existing with { ItemId = itemId, LastPingedAt = DateTimeOffset.UtcNow });
    }

    public void Ping(string playSessionId) {
        if (string.IsNullOrWhiteSpace(playSessionId)) {
            return;
        }

        _sessions.AddOrUpdate(
            playSessionId,
            _ => new ActiveTranscodeSession(Guid.Empty, DateTimeOffset.UtcNow),
            (_, existing) => existing with { LastPingedAt = DateTimeOffset.UtcNow });
    }

    public bool IsRegisteredForItem(string playSessionId, Guid itemId) =>
        !string.IsNullOrWhiteSpace(playSessionId) &&
        _sessions.TryGetValue(playSessionId, out var session) &&
        session.ItemId == itemId;

    public Task CancelAsync(string playSessionId, CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(playSessionId)) {
            if (_sessions.TryRemove(playSessionId, out var session)) {
                HlsAssetService.CancelActiveGenerationsForItem(session.ItemId);
            }
        }

        return Task.CompletedTask;
    }

    public Task<int> CancelAllAsync(CancellationToken cancellationToken) {
        var count = _sessions.Count;
        _sessions.Clear();
        var activeHlsGenerations = HlsAssetService.CancelAllActiveGenerations();
        return Task.FromResult(count + activeHlsGenerations);
    }

    public IReadOnlySet<Guid> LiveItemIds(TimeSpan within) {
        var cutoff = DateTimeOffset.UtcNow - within;
        var live = new HashSet<Guid>();
        foreach (var session in _sessions.Values) {
            if (session.ItemId != Guid.Empty && session.LastPingedAt >= cutoff) {
                live.Add(session.ItemId);
            }
        }

        return live;
    }

    public int ReapStaleSessions(TimeSpan ttl) {
        var cutoff = DateTimeOffset.UtcNow - ttl;
        var removed = 0;
        foreach (var (playSessionId, session) in _sessions) {
            if (session.LastPingedAt < cutoff && _sessions.TryRemove(playSessionId, out _)) {
                removed++;
            }
        }

        return removed;
    }

    private sealed record ActiveTranscodeSession(Guid ItemId, DateTimeOffset LastPingedAt);
}
