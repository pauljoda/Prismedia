using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Keeps short-lived request reviews alive while their provider cascade runs outside the initiating
/// HTTP request. Equivalent retries share one session, preventing slow clients from starting duplicate
/// plugin trees when a page is refreshed or its connection closes.
/// </summary>
public sealed class RequestReviewPreparationService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime) : IRequestReviewPreparationService {
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<Guid, ReviewState> _reviews = new();
    private readonly ConcurrentDictionary<ReviewKey, Guid> _reviewIds = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);

    /// <inheritdoc />
    public async Task<RequestReviewResponse?> StartAsync(
        RequestReviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        CleanupExpired();
        var key = ReviewKey.From(request, hideNsfw);
        await _startGate.WaitAsync(cancellationToken);
        try {
            if (TryReusable(key, out var existing)) {
                return existing.Snapshot();
            }

            var scope = scopeFactory.CreateAsyncScope();
            try {
                var source = scope.ServiceProvider.GetRequiredService<IPluginRequestProgressiveReviewSource>();
                var seed = await source.StartReviewAsync(request, hideNsfw, cancellationToken);
                if (seed is null) {
                    await scope.DisposeAsync();
                    return null;
                }

                var pending = PendingProposalIds(seed.Proposal);
                var state = new ReviewState(Guid.NewGuid(), key, seed, pending);
                _reviews[state.Id] = state;
                _reviewIds[key] = state.Id;
                if (pending.Count == 0) {
                    state.Complete(seed);
                    await scope.DisposeAsync();
                } else {
                    _ = Task.Run(
                        () => EnrichAsync(scope, source, state, hideNsfw),
                        CancellationToken.None);
                }

                return state.Snapshot();
            } catch {
                await scope.DisposeAsync();
                throw;
            }
        } finally {
            _startGate.Release();
        }
    }

    /// <inheritdoc />
    public RequestReviewResponse? Get(Guid reviewId) {
        CleanupExpired();
        return _reviews.TryGetValue(reviewId, out var state) ? state.Snapshot() : null;
    }

    private async Task EnrichAsync(
        AsyncServiceScope scope,
        IPluginRequestProgressiveReviewSource source,
        ReviewState state,
        bool hideNsfw) {
        try {
            var completed = await source.EnrichReviewAsync(
                state.Review,
                hideNsfw,
                (update, _) => {
                    state.Update(update);
                    return Task.CompletedTask;
                },
                lifetime.ApplicationStopping);
            state.Complete(completed);
        } catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested) {
            state.Fail("Request review enrichment stopped because Prismedia is shutting down.");
        } catch (Exception exception) {
            state.Fail($"Some request details could not be identified: {exception.Message}");
        } finally {
            await scope.DisposeAsync();
        }
    }

    private bool TryReusable(ReviewKey key, out ReviewState state) {
        if (_reviewIds.TryGetValue(key, out var id)
            && _reviews.TryGetValue(id, out state!)
            && state.CanReuse) {
            return true;
        }

        _reviewIds.TryRemove(key, out _);
        state = null!;
        return false;
    }

    private void CleanupExpired() {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        foreach (var (id, state) in _reviews) {
            if (!state.IsExpired(cutoff) || !_reviews.TryRemove(id, out _)) {
                continue;
            }

            _reviewIds.TryRemove(new KeyValuePair<ReviewKey, Guid>(state.Key, id));
        }
    }

    private static IReadOnlyList<string> PendingProposalIds(EntityMetadataProposal proposal) =>
        EntityMetadataProposalTraversal.Relationships(proposal)
            .Concat((proposal.Children ?? []).Where(child => child.TargetKind.IsRelationship()))
            .Concat(EntityMetadataProposalTraversal.StructuralChildren(proposal))
            .Select(node => node.ProposalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private sealed record ReviewKey(
        RequestMediaKind Kind,
        string PluginId,
        string IdentityNamespace,
        string IdentityValue,
        bool HideNsfw) {
        public static ReviewKey From(RequestReviewRequest request, bool hideNsfw) =>
            new(
                request.Kind,
                request.PluginId.Trim().ToLowerInvariant(),
                request.ExternalIdentity.Namespace.Trim().ToLowerInvariant(),
                request.ExternalIdentity.Value,
                hideNsfw);
    }

    private sealed class ReviewState(
        Guid id,
        ReviewKey key,
        RequestReviewResponse review,
        IReadOnlyList<string> pending) {
        private readonly object _gate = new();
        private readonly HashSet<string> _pending = new(pending, StringComparer.Ordinal);
        private RequestReviewResponse _review = review;
        private bool _running = pending.Count > 0;
        private string? _error;
        private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

        public Guid Id { get; } = id;
        public ReviewKey Key { get; } = key;
        public RequestReviewResponse Review => _review;

        public bool CanReuse {
            get {
                lock (_gate) {
                    return _error is null;
                }
            }
        }

        public RequestReviewResponse Snapshot() {
            lock (_gate) {
                return _review with {
                    Enrichment = new RequestReviewEnrichment(
                        Id,
                        _running,
                        _pending.ToArray(),
                        _error,
                        _updatedAt)
                };
            }
        }

        public void Update(RequestReviewProgressUpdate update) {
            lock (_gate) {
                _review = update.Review;
                _pending.Remove(update.CompletedProposalId);
                _updatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void Complete(RequestReviewResponse completed) {
            lock (_gate) {
                _review = completed;
                _pending.Clear();
                _running = false;
                _updatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void Fail(string error) {
            lock (_gate) {
                _pending.Clear();
                _running = false;
                _error = error;
                _updatedAt = DateTimeOffset.UtcNow;
            }
        }

        public bool IsExpired(DateTimeOffset cutoff) {
            lock (_gate) {
                return !_running && _updatedAt < cutoff;
            }
        }
    }
}
