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

    private sealed record ActiveTranscodeSession(Guid ItemId, DateTimeOffset LastPingedAt);
}
