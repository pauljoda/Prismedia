using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Owns the durable identify queue state machine for entity metadata review.
/// </summary>
public sealed class IdentifyQueueService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly PrismediaDbContext _db;
    private readonly IdentifyPluginService _identify;

    public IdentifyQueueService(PrismediaDbContext db, IdentifyPluginService identify) {
        _db = db;
        _identify = identify;
    }

    /// <summary>
    /// Lists active identify queue items, optionally including terminal history rows.
    /// </summary>
    public async Task<IReadOnlyList<IdentifyQueueItem>> ListAsync(
        bool includeCompleted,
        CancellationToken cancellationToken) {
        var query = _db.IdentifyQueueItems.AsNoTracking();
        if (!includeCompleted) {
            query = query.Where(row => row.State != IdentifyQueueState.Done && row.State != IdentifyQueueState.Deleted);
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
            hideNsfw,
            cancellationToken);

        row.ProviderCode = request.Provider;
        row.Action = GuessAction(request.Query);
        row.QueryJson = request.Query is null ? null : JsonSerializer.Serialize(request.Query, JsonOptions);
        row.UpdatedAt = now;
        row.CompletedAt = null;

        if (!response.Ok) {
            row.State = IdentifyQueueState.Error;
            row.Error = response.Error ?? "Identify failed.";
            row.CandidatesJson = null;
            row.ProposalJson = null;
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
        var proposal = request.Proposal ?? Deserialize<EntityMetadataProposal>(row.ProposalJson)
            ?? throw new InvalidOperationException("Identify queue item has no proposal to apply.");

        var applied = await _identify.ApplyAsync(
            entityId,
            proposal,
            request.SelectedFields,
            request.SelectedImages,
            cancellationToken);
        if (!applied) {
            throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        row.State = IdentifyQueueState.Done;
        row.ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions);
        row.Error = null;
        row.UpdatedAt = now;
        row.CompletedAt = now;

        var flags = await _db.EntityFlags.FindAsync([entityId], cancellationToken);
        if (flags != null) {
            flags.IsOrganized = true;
            flags.UpdatedAt = now;
        } else {
            _db.EntityFlags.Add(new EntityFlagRow {
                EntityId = entityId,
                IsOrganized = true,
                UpdatedAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

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

    private static T? Deserialize<T>(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
