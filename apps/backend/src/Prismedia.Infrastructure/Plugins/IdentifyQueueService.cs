using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Owns the durable identify queue state machine for entity metadata review.
/// </summary>
public sealed class IdentifyQueueService : IIdentifyQueueService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly PrismediaDbContext _db;
    private readonly IdentifyPluginService _identify;
    private readonly IIdentifyApplyProgressStore _progress;

    public IdentifyQueueService(
        PrismediaDbContext db,
        IdentifyPluginService identify,
        IIdentifyApplyProgressStore progress) {
        _db = db;
        _identify = identify;
        _progress = progress;
    }

    /// <summary>
    /// Lists active identify queue items, optionally including terminal history rows.
    /// </summary>
    public async Task<IReadOnlyList<IdentifyQueueItem>> ListAsync(
        bool includeCompleted,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var query = _db.IdentifyQueueItems.AsNoTracking();
        if (!includeCompleted) {
            query = query.Where(row => row.State != IdentifyQueueState.Done && row.State != IdentifyQueueState.Deleted);
        }
        if (hideNsfw) {
            query = query.Where(row => !_db.Entities.Any(entity => entity.Id == row.EntityId && entity.IsNsfw));
        }

        var rows = await query
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);

        return await MapRowsAsync(rows, cancellationToken);
    }

    /// <summary>
    /// Gets the queue item for an entity, or null when the entity is not queued.
    /// </summary>
    public async Task<IdentifyQueueItem?> GetAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is null) {
            return null;
        }

        return await MapRowAsync(row, cancellationToken);
    }

    /// <summary>
    /// Adds an entity to the identify queue, preserving active work and resetting terminal items.
    /// </summary>
    public async Task<IdentifyQueueItem> AddAsync(Guid entityId, CancellationToken cancellationToken) {
        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var now = DateTimeOffset.UtcNow;
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);

        if (row is null) {
            row = new IdentifyQueueItemRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                State = IdentifyQueueState.Search,
                Action = "search",
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.IdentifyQueueItems.Add(row);
        } else if (row.State is IdentifyQueueState.Done or IdentifyQueueState.Deleted) {
            ResetForSearch(row, now);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    /// <summary>
    /// Runs a provider search for the queued entity and persists candidates or a hydrated proposal.
    /// </summary>
    public async Task<IdentifyQueueItem> SearchAsync(
        Guid entityId,
        IdentifyQueueSearchRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        var row = await EnsureMutableRowAsync(entityId, cancellationToken);
        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var now = DateTimeOffset.UtcNow;
        var response = await _identify.IdentifyAsync(
            entityId,
            request.Provider,
            request.Query,
            parentExternalIds: null,
            hideNsfw,
            cancellationToken);

        row.ProviderCode = request.Provider;
        row.Action = GuessAction(request.Query);
        row.QueryJson = request.Query is null ? null : JsonSerializer.Serialize(request.Query, JsonOptions);
        row.UpdatedAt = now;
        row.CompletedAt = null;

        var requireChoice = request.Query?.RequireChoice == true;
        if (!response.Ok) {
            row.State = IdentifyQueueState.Error;
            row.Error = response.Error ?? "Identify failed.";
            row.CandidatesJson = null;
            row.ProposalJson = null;
        } else if (requireChoice && response.Result is not null) {
            var candidates = ChoiceCandidates(response.Result, entity);
            if (candidates.Count > 0) {
                row.State = IdentifyQueueState.Search;
                row.Error = null;
                row.CandidatesJson = JsonSerializer.Serialize(candidates, JsonOptions);
                row.ProposalJson = null;
            } else {
                row.State = IdentifyQueueState.Error;
                row.Error = response.Error ?? "No provider match was found.";
                row.CandidatesJson = null;
                row.ProposalJson = null;
            }
        } else if (response.Result?.Patch is not null) {
            row.State = IdentifyQueueState.Proposal;
            row.Error = null;
            row.CandidatesJson = null;
            row.ProposalJson = JsonSerializer.Serialize(response.Result, JsonOptions);
        } else if (response.Result?.Candidates is { Count: > 0 } candidates) {
            row.State = IdentifyQueueState.Search;
            row.Error = null;
            row.CandidatesJson = JsonSerializer.Serialize(candidates, JsonOptions);
            row.ProposalJson = null;
        } else {
            row.State = IdentifyQueueState.Error;
            row.Error = response.Error ?? "No provider match was found.";
            row.CandidatesJson = null;
            row.ProposalJson = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    /// <summary>
    /// Applies the reviewed proposal and marks the queue item as done.
    /// </summary>
    public async Task<IdentifyQueueItem> ApplyAsync(
        Guid entityId,
        ApplyIdentifyQueueItemRequest request,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Identify queue item for entity '{entityId}' was not found.");
        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var storedProposal = Deserialize<EntityMetadataProposal>(row.ProposalJson)
            ?? throw new InvalidOperationException("Identify queue item has no proposal to apply.");
        var proposal = request.Proposal ?? storedProposal;
        if (!string.Equals(proposal.ProposalId, storedProposal.ProposalId, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Only the root identify proposal can be applied to a queue item.");
        }
        if (!string.Equals(proposal.TargetKind, entity.KindCode, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Identify proposal kind does not match the queued entity.");
        }
        var acceptedProposal = MarkAcceptedProposalTreeOrganized(proposal);
        IdentifyApplyProgressReporter? progressReporter = null;
        if (request.ProgressId is { } progressId) {
            _progress.Begin(progressId, entityId, CountApplySteps(acceptedProposal, request.SelectedFields));
            progressReporter = new IdentifyApplyProgressReporter(_progress, progressId);
        }

        try {
            var applied = await _identify.ApplyAsync(
                entityId,
                acceptedProposal,
                request.SelectedFields,
                request.SelectedImages,
                progressReporter,
                cancellationToken);
            if (!applied) {
                throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            row.State = IdentifyQueueState.Done;
            row.ProposalJson = JsonSerializer.Serialize(acceptedProposal, JsonOptions);
            row.Error = null;
            row.UpdatedAt = now;
            row.CompletedAt = now;

            var entityRow = await _db.Entities.FindAsync([entityId], cancellationToken);
            if (entityRow is not null) {
                entityRow.IsOrganized = true;
                entityRow.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            if (request.ProgressId is { } completedProgressId) {
                _progress.Complete(completedProgressId);
            }
        } catch (Exception ex) when (request.ProgressId is { } failedProgressId) {
            _progress.Fail(failedProgressId, ex.Message);
            throw;
        }

        var refreshedEntity = await LoadEntityAsync(entityId, cancellationToken) ?? entity;
        return MapRow(row, refreshedEntity);
    }

    /// <summary>
    /// Removes an item from the active identify queue without applying metadata.
    /// </summary>
    public async Task<IdentifyQueueItem?> DeleteAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is null) {
            return null;
        }

        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var now = DateTimeOffset.UtcNow;
        row.State = IdentifyQueueState.Deleted;
        row.Error = null;
        row.UpdatedAt = now;
        row.CompletedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    private async Task<IdentifyQueueItemRow> EnsureMutableRowAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is not null) {
            return row;
        }

        await AddAsync(entityId, cancellationToken);
        return await _db.IdentifyQueueItems
            .FirstAsync(item => item.EntityId == entityId, cancellationToken);
    }

    private async Task<IReadOnlyList<IdentifyQueueItem>> MapRowsAsync(
        IReadOnlyList<IdentifyQueueItemRow> rows,
        CancellationToken cancellationToken) {
        var entityIds = rows.Select(row => row.EntityId).Distinct().ToArray();
        var entities = await _db.Entities
            .AsNoTracking()
            .Where(entity => entityIds.Contains(entity.Id))
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);
        return rows
            .Where(row => entities.ContainsKey(row.EntityId))
            .Select(row => MapRow(row, entities[row.EntityId]))
            .ToArray();
    }

    private async Task<IdentifyQueueItem> MapRowAsync(IdentifyQueueItemRow row, CancellationToken cancellationToken) {
        var entity = await LoadEntityAsync(row.EntityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{row.EntityId}' was not found.");
        return MapRow(row, entity);
    }

    private static IdentifyQueueItem MapRow(IdentifyQueueItemRow row, EntityRow entity) =>
        new(
            row.Id,
            row.EntityId,
            entity.KindCode,
            entity.Title,
            entity.IsNsfw,
            row.State.ToCode(),
            row.ProviderCode,
            row.Action,
            Deserialize<IdentifyQuery>(row.QueryJson),
            Deserialize<IReadOnlyList<EntitySearchCandidate>>(row.CandidatesJson) ?? [],
            Deserialize<EntityMetadataProposal>(row.ProposalJson),
            row.Error,
            row.CreatedAt,
            row.UpdatedAt,
            row.CompletedAt);

    private async Task<EntityRow?> LoadEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        await _db.Entities
            .FirstOrDefaultAsync(entity => entity.Id == entityId && entity.DeletedAt == null, cancellationToken);

    private static void ResetForSearch(IdentifyQueueItemRow row, DateTimeOffset now) {
        row.State = IdentifyQueueState.Search;
        row.ProviderCode = null;
        row.Action = "search";
        row.QueryJson = null;
        row.CandidatesJson = null;
        row.ProposalJson = null;
        row.Error = null;
        row.UpdatedAt = now;
        row.CompletedAt = null;
    }

    private static string GuessAction(IdentifyQuery? query) {
        if (query?.ExternalIds is { Count: > 0 }) {
            return "lookup-id";
        }

        if (!string.IsNullOrWhiteSpace(query?.Url)) {
            return "lookup-url";
        }

        return "search";
    }

    private static IReadOnlyList<EntitySearchCandidate> ChoiceCandidates(EntityMetadataProposal proposal, EntityRow entity) {
        if (proposal.Candidates is { Count: > 0 }) {
            return proposal.Candidates;
        }

        if (proposal.Patch is null) {
            return [];
        }

        var title = !string.IsNullOrWhiteSpace(proposal.Patch.Title)
            ? proposal.Patch.Title.Trim()
            : entity.Title;
        var poster = proposal.Images.FirstOrDefault(image => image.Kind is "poster" or "still")?.Url;
        return [
            new EntitySearchCandidate(
                proposal.Patch.ExternalIds,
                title,
                YearFromDates(proposal.Patch.Dates),
                proposal.Patch.Description,
                poster,
                Popularity: null,
                CandidateId: proposal.ProposalId,
                Source: proposal.Provider,
                Confidence: proposal.Confidence,
                MatchReason: proposal.MatchReason)
        ];
    }

    private static int? YearFromDates(IReadOnlyDictionary<string, string> dates) {
        foreach (var key in new[] { "release", "firstAir", "airDate", "date" }) {
            if (dates.TryGetValue(key, out var value) &&
                DateOnly.TryParse(value, out var parsed)) {
                return parsed.Year;
            }
        }

        return null;
    }

    private static EntityMetadataProposal MarkAcceptedProposalTreeOrganized(EntityMetadataProposal proposal) {
        var children = proposal.Children.Select(MarkAcceptedProposalTreeOrganized).ToArray();
        var relationships = (proposal.Relationships ?? []).Select(MarkAcceptedProposalTreeOrganized).ToArray();

        if (proposal.Patch is null) {
            return proposal with {
                Children = children,
                Relationships = relationships
            };
        }

        return proposal with {
            Patch = proposal.Patch with {
                Flags = MarkOrganized(proposal.Patch.Flags)
            },
            Children = children,
            Relationships = relationships
        };
    }

    private static EntityMetadataFlagsPatch MarkOrganized(EntityMetadataFlagsPatch? flags) =>
        flags is null
            ? new EntityMetadataFlagsPatch(null, null, true)
            : flags with { IsOrganized = true };

    private static int CountApplySteps(EntityMetadataProposal proposal, IReadOnlyCollection<string> selectedFields) {
        var selected = selectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count = 1;
        if (selected.Contains("credits") || selected.Contains("studio") || selected.Contains("tags")) {
            count += CountRelationships(proposal);
        }

        count += proposal.Children
            .Where(child => !EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind))
            .Sum(CountStructuralApplySteps);
        return Math.Max(count, 1);
    }

    private static int CountStructuralApplySteps(EntityMetadataProposal proposal) {
        var count = 1;
        if (proposal.Patch.Credits.Count > 0 ||
            !string.IsNullOrWhiteSpace(proposal.Patch.Studio) ||
            proposal.Patch.Tags.Count > 0) {
            count += CountRelationships(proposal);
        }

        count += proposal.Children
            .Where(child => !EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind))
            .Sum(CountStructuralApplySteps);
        return count;
    }

    private static int CountRelationships(EntityMetadataProposal proposal) =>
        (proposal.Relationships ?? [])
            .Where(child => EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind))
            .GroupBy(child => child.ProposalId, StringComparer.Ordinal)
            .Count();

    private static T? Deserialize<T>(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
