using System.Collections.Concurrent;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Entity-level progress step emitted while a reviewed Identify proposal is being applied.
/// </summary>
/// <param name="Kind">Entity kind for the entity currently being updated.</param>
/// <param name="Title">Display title for the entity currently being updated.</param>
/// <param name="Path">Structural path from the accepted root proposal to the current entity.</param>
public sealed record IdentifyApplyProgressStep(
    EntityKind Kind,
    string Title,
    IReadOnlyList<string> Path);

/// <summary>
/// Stores short-lived progress for synchronous Identify proposal apply requests.
/// </summary>
public interface IIdentifyApplyProgressStore {
    /// <summary>Starts tracking a proposal apply operation.</summary>
    IdentifyApplyProgress Begin(Guid operationId, Guid entityId, int total);

    /// <summary>Records the entity currently being applied for an operation.</summary>
    IdentifyApplyProgress ReportStep(Guid operationId, IdentifyApplyProgressStep step);

    /// <summary>Marks an operation as successfully completed.</summary>
    IdentifyApplyProgress Complete(Guid operationId);

    /// <summary>Marks an operation as failed with a user-facing error message.</summary>
    IdentifyApplyProgress Fail(Guid operationId, string message);

    /// <summary>Gets the latest progress snapshot for an operation, or null when it is unknown or expired.</summary>
    IdentifyApplyProgress? Get(Guid operationId);
}

/// <summary>
/// Convenience reporter bound to one Identify apply operation.
/// </summary>
public sealed class IdentifyApplyProgressReporter {
    private readonly IIdentifyApplyProgressStore _store;
    private readonly Guid _operationId;

    /// <summary>
    /// Creates a reporter for one apply operation.
    /// </summary>
    /// <param name="store">Progress store receiving snapshots.</param>
    /// <param name="operationId">Client-supplied apply operation identifier.</param>
    public IdentifyApplyProgressReporter(IIdentifyApplyProgressStore store, Guid operationId) {
        _store = store;
        _operationId = operationId;
    }

    /// <summary>
    /// Records the entity currently being updated.
    /// </summary>
    /// <param name="kind">Entity kind.</param>
    /// <param name="title">Display title.</param>
    /// <param name="path">Structural path from the root proposal.</param>
    public void ReportEntity(EntityKind kind, string title, IReadOnlyList<string> path) =>
        _store.ReportStep(_operationId, new IdentifyApplyProgressStep(kind, title, path));
}

/// <summary>
/// In-memory implementation for live Identify apply progress. Snapshots are intentionally
/// short-lived because the durable queue item stores the final accepted proposal.
/// </summary>
public sealed class InMemoryIdentifyApplyProgressStore : IIdentifyApplyProgressStore {
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<Guid, IdentifyApplyProgress> _progress = new();

    /// <inheritdoc />
    public IdentifyApplyProgress Begin(Guid operationId, Guid entityId, int total) {
        PruneExpired();
        var snapshot = new IdentifyApplyProgress(
            operationId,
            entityId,
            "running",
            0,
            Math.Max(total, 1),
            null,
            null,
            [],
            null,
            DateTimeOffset.UtcNow);
        _progress[operationId] = snapshot;
        return snapshot;
    }

    /// <inheritdoc />
    public IdentifyApplyProgress ReportStep(Guid operationId, IdentifyApplyProgressStep step) {
        var updated = _progress.AddOrUpdate(
            operationId,
            _ => new IdentifyApplyProgress(
                operationId,
                Guid.Empty,
                "running",
                1,
                1,
                step.Kind,
                step.Title,
                step.Path,
                null,
                DateTimeOffset.UtcNow),
            (_, current) => current with {
                State = "running",
                CurrentIndex = Math.Min(current.CurrentIndex + 1, current.Total),
                CurrentKind = step.Kind,
                CurrentTitle = step.Title,
                CurrentPath = step.Path,
                Error = null,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        return updated;
    }

    /// <inheritdoc />
    public IdentifyApplyProgress Complete(Guid operationId) =>
        UpdateTerminal(operationId, "succeeded", null);

    /// <inheritdoc />
    public IdentifyApplyProgress Fail(Guid operationId, string message) =>
        UpdateTerminal(operationId, "failed", message);

    /// <inheritdoc />
    public IdentifyApplyProgress? Get(Guid operationId) {
        if (!_progress.TryGetValue(operationId, out var snapshot)) {
            return null;
        }

        if (DateTimeOffset.UtcNow - snapshot.UpdatedAt <= Retention) {
            return snapshot;
        }

        _progress.TryRemove(operationId, out _);
        return null;
    }

    private IdentifyApplyProgress UpdateTerminal(Guid operationId, string state, string? error) {
        var updated = _progress.AddOrUpdate(
            operationId,
            _ => new IdentifyApplyProgress(
                operationId,
                Guid.Empty,
                state,
                1,
                1,
                null,
                null,
                [],
                error,
                DateTimeOffset.UtcNow),
            (_, current) => current with {
                State = state,
                CurrentIndex = state == "succeeded" ? current.Total : current.CurrentIndex,
                Error = error,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        return updated;
    }

    private void PruneExpired() {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        foreach (var (id, snapshot) in _progress) {
            if (snapshot.UpdatedAt < cutoff) {
                _progress.TryRemove(id, out _);
            }
        }
    }
}
