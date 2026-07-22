using System.Collections.Concurrent;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>One transient, server-held replacement review. It is intentionally not persisted.</summary>
public sealed record ManualReplacementSearchSession(
    Guid Id,
    Guid EntityId,
    IReadOnlyList<ReviewedReleaseCandidate> Candidates,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Short-lived in-memory review storage. Search results include server-only download links, so the client
/// receives only display DTOs and returns an opaque search/candidate pair when it makes an explicit choice.
/// Queue attempts for one entity are serialized, while the session remains replayable until expiry; the
/// durable reviewed child is the idempotency boundary after materialization.
/// </summary>
public sealed class ManualReplacementSearchSessionStore {
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<Guid, ManualReplacementSearchSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _entityQueueGates = new();

    public ManualReplacementSearchSession Create(
        Guid entityId,
        IReadOnlyList<ReviewedReleaseCandidate> candidates) {
        PruneExpired();
        var session = new ManualReplacementSearchSession(
            Guid.NewGuid(),
            entityId,
            candidates,
            DateTimeOffset.UtcNow + Lifetime);
        _sessions[session.Id] = session;
        return session;
    }

    /// <summary>
    /// Runs one queue attempt under the entity's in-process serialization gate without consuming its review.
    /// Null means the review is missing, expired, or belongs to another entity.
    /// </summary>
    public async Task<T?> ExecuteExclusiveAsync<T>(
        Guid searchId,
        Guid entityId,
        Func<ManualReplacementSearchSession, Task<T>> action,
        CancellationToken cancellationToken = default) where T : class {
        PruneExpired();
        if (!_sessions.TryGetValue(searchId, out var session)
            || session.EntityId != entityId
            || session.ExpiresAt <= DateTimeOffset.UtcNow) {
            return null;
        }

        var gate = _entityQueueGates.GetOrAdd(entityId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try {
            PruneExpired();
            if (!_sessions.TryGetValue(searchId, out session)
                || session.EntityId != entityId
                || session.ExpiresAt <= DateTimeOffset.UtcNow) {
                return null;
            }

            return await action(session);
        } finally {
            gate.Release();
        }
    }

    private void PruneExpired() {
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _sessions.Values.Where(value => value.ExpiresAt <= now)) {
            _sessions.TryRemove(session.Id, out _);
        }
    }
}

/// <summary>
/// Runs reviewed replacements without touching durable state until selection. Once selected, it creates the
/// same upgrade-child lifecycle used by automatic upgrades and queues only the chosen release.
/// </summary>
public sealed class ManualReplacementService(
    IManualReplacementStore replacements,
    AcquisitionSearchRunner searches,
    ManualReplacementSearchSessionStore sessions,
    IAcquisitionQueueService queue) {
    public async Task<ManualReplacementSearchResult> SearchAsync(
        Guid entityId,
        string? customQuery,
        CancellationToken cancellationToken) {
        var target = await replacements.GetSearchTargetAsync(entityId, cancellationToken)
            ?? throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "This item does not have a replaceable on-disk file.");
        var outcome = await searches.RunAsync(
            target.Input,
            cancellationToken,
            target.OwnedQuality,
            customQuery);
        var candidates = outcome.Candidates
            .Select(candidate => new ReviewedReleaseCandidate(Guid.NewGuid(), candidate))
            .ToArray();
        var session = sessions.Create(entityId, candidates);
        return new ManualReplacementSearchResult(
            session.Id,
            candidates.Select(ToView).ToArray());
    }

    public async Task<AcquisitionDetail> QueueAsync(
        Guid entityId,
        Guid searchId,
        Guid candidateId,
        CancellationToken cancellationToken) {
        var detail = await sessions.ExecuteExclusiveAsync(
            searchId,
            entityId,
            async session => {
                if (!session.Candidates.Any(candidate => candidate.Id == candidateId)) {
                    throw new AcquisitionConfigurationException(
                        ApiProblemCodes.AcquisitionReleaseNotFound,
                        "The selected replacement release was not part of this review.");
                }

                var childId = await replacements.CreateReviewedReplacementAsync(
                    entityId,
                    session.Candidates,
                    cancellationToken)
                    ?? throw new AcquisitionConfigurationException(
                        ApiProblemCodes.AcquisitionInvalid,
                        "Another replacement is already in progress for this item.");
                return await queue.QueueAsync(
                        childId,
                        candidateId,
                        cancellationToken,
                        manualPick: true)
                    ?? throw new AcquisitionConfigurationException(
                        ApiProblemCodes.AcquisitionNotFound,
                        "The replacement acquisition no longer exists.");
            },
            cancellationToken);
        return detail
            ?? throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "This replacement review expired. Search again before choosing a release.");
    }

    private static ReleaseCandidateView ToView(ReviewedReleaseCandidate candidate) {
        var release = candidate.Release.Release;
        return new ReleaseCandidateView(
            candidate.Id,
            candidate.Release.IndexerName,
            release.Title,
            release.SizeBytes,
            release.Seeders,
            release.Peers,
            release.Protocol,
            candidate.Release.Accepted,
            candidate.Release.Score,
            candidate.Release.Rejections,
            release.InfoUrl,
            release.PublishedAt);
    }
}
