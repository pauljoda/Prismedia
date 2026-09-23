using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Projects a bounded activity feed from retained local integration journals.</summary>
public sealed class EfRequestActivityReader(
    PrismediaDbContext db,
    TransferPlanProtector transferPlans,
    TimeProvider timeProvider) : IRequestActivityReader {
    private const int DefaultLimit = 50;
    private const int MaximumLimit = 100;
    private const int TransferRank = 0;
    private const int RequestRank = 1;
    private const int HoldingRank = 2;
    private const int AttentionGroupRank = 0;
    private const int ProgressGroupRank = 1;
    private const int FollowingGroupRank = 2;
    private const int RecentGroupRank = 3;
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    /// <inheritdoc />
    public async Task<RequestActivityPage> ListAsync(
        Guid? connectionId,
        string? cursor,
        int limit,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var pageSize = limit <= 0 ? DefaultLimit : Math.Min(limit, MaximumLimit);
        var after = ActivityCursor.Decode(cursor, connectionId, hideNsfw);
        var candidateLimit = pageSize + 1;

        var transfers = await TransferCandidates(connectionId, after, candidateLimit)
            .ToArrayAsync(cancellationToken);
        var requests = await RequestCandidates(connectionId, after, candidateLimit, hideNsfw)
            .ToArrayAsync(cancellationToken);
        var holdings = await HoldingCandidates(connectionId, after, candidateLimit, hideNsfw)
            .ToArrayAsync(cancellationToken);

        var scan = transfers.Select(Candidate.Transfer)
            .Concat(requests.Select(Candidate.Request))
            .Concat(holdings.Select(Candidate.Holding))
            .OrderBy(candidate => candidate.GroupRank)
            .ThenByDescending(candidate => candidate.SortAt)
            .ThenBy(candidate => candidate.TypeRank)
            .ThenByDescending(candidate => candidate.Id.ToString(), StringComparer.Ordinal)
            .Take(candidateLimit)
            .ToArray();
        var scannedPage = scan.Take(pageSize).ToArray();
        var mapped = await MapAsync(scannedPage, hideNsfw, cancellationToken);
        var sources = await SourceHealthAsync(connectionId, cancellationToken);
        var nextCursor = scan.Length > pageSize
            ? ActivityCursor.Encode(scannedPage[^1].SortAt, scannedPage[^1].GroupRank,
                scannedPage[^1].TypeRank, scannedPage[^1].Id,
                connectionId, hideNsfw)
            : null;
        return new(mapped, sources, nextCursor, timeProvider.GetUtcNow());
    }

    private IQueryable<TimedTransfer> TransferCandidates(
        Guid? connectionId,
        ActivityCursor? after,
        int limit) {
        var query =
            from row in db.IntegrationTransfers.AsNoTracking()
            let groupRank = row.Phase == IntegrationTransferPhase.Completed || row.Phase == IntegrationTransferPhase.Cancelled
                ? RecentGroupRank
                : row.LastError != null || row.Phase == IntegrationTransferPhase.NeedsReview || row.Phase == IntegrationTransferPhase.Failed
                    ? AttentionGroupRank
                    : ProgressGroupRank
            where connectionId == null || row.ConnectionId == connectionId
            where after == null || groupRank > after.GroupRank
                || groupRank == after.GroupRank && (row.CreatedAt < after.SortAt
                    || row.CreatedAt == after.SortAt && (TransferRank > after.TypeRank
                        || TransferRank == after.TypeRank && row.Id.ToString().CompareTo(after.Id.ToString()) < 0))
            orderby groupRank, row.CreatedAt descending, row.Id.ToString() descending
            select new TimedTransfer(row, row.UpdatedAt, row.CreatedAt, groupRank);
        return query.Take(limit);
    }

    private IQueryable<TimedRequest> RequestCandidates(
        Guid? connectionId,
        ActivityCursor? after,
        int limit,
        bool hideNsfw) {
        var supportsJsonContainment = db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;
        var query =
            from row in db.ManagedRequests.AsNoTracking()
            let reviewRequired = supportsJsonContainment
                ? EF.Functions.JsonContains(row.StateJson, """{"reviewRequired":true}""")
                : row.StateJson.Contains("\"reviewRequired\":true", StringComparison.Ordinal)
            let groupRank = row.Phase == ManagedRequestPhase.Completed || row.Phase == ManagedRequestPhase.Cancelled
                || row.Phase == ManagedRequestPhase.OwnershipReleased || row.Phase == ManagedRequestPhase.RemoteRemoved
                    ? RecentGroupRank
                    : reviewRequired || row.Phase == ManagedRequestPhase.CreationUncertain
                        || row.Phase == ManagedRequestPhase.Rejected
                            ? AttentionGroupRank
                            : ProgressGroupRank
            where connectionId == null || row.ConnectionId == connectionId
            where reviewRequired || !db.ManagedHoldings.Any(holding => holding.Id == row.Id)
            where !hideNsfw || !db.LibraryRoots.Any(root => root.Id == row.LibraryRootId && root.IsNsfw)
            where !hideNsfw || !db.Entities.Any(entity => entity.Id == row.EntityId && entity.IsNsfw)
            where after == null || groupRank > after.GroupRank
                || groupRank == after.GroupRank && (row.CreatedAt < after.SortAt
                    || row.CreatedAt == after.SortAt && (RequestRank > after.TypeRank
                        || RequestRank == after.TypeRank && row.Id.ToString().CompareTo(after.Id.ToString()) < 0))
            orderby groupRank, row.CreatedAt descending, row.Id.ToString() descending
            select new TimedRequest(row, row.UpdatedAt, row.CreatedAt, groupRank);
        return query.Take(limit);
    }

    private IQueryable<TimedHolding> HoldingCandidates(
        Guid? connectionId,
        ActivityCursor? after,
        int limit,
        bool hideNsfw) {
        var supportsJsonContainment = db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;
        var query =
            from holding in db.ManagedHoldings.AsNoTracking()
            join request in db.ManagedRequests.AsNoTracking() on holding.Id equals request.Id into matchedRequests
            from request in matchedRequests.DefaultIfEmpty()
            let requestNeedsReview = request != null && (supportsJsonContainment
                ? EF.Functions.JsonContains(request.StateJson, """{"reviewRequired":true}""")
                : request.StateJson.Contains("\"reviewRequired\":true", StringComparison.Ordinal))
            let occurredAt = holding.LastCheckedAt ?? holding.ReleasedAt
                ?? (request == null ? (DateTimeOffset?)null : request.CreatedAt)
                ?? holding.NextCheckAt
            let sortAt = request == null ? DateTimeOffset.UnixEpoch : request.CreatedAt
            let groupRank = holding.Status == ManagedTrackingStatus.Released || holding.Status == ManagedTrackingStatus.Removed
                ? RecentGroupRank
                : holding.Problem != null || holding.Status == ManagedTrackingStatus.NeedsReview || holding.Status == ManagedTrackingStatus.Stale
                    ? AttentionGroupRank
                    : holding.Status == ManagedTrackingStatus.Tracking
                        ? FollowingGroupRank
                        : ProgressGroupRank
            where connectionId == null || holding.ConnectionId == connectionId
            where !requestNeedsReview
            where !hideNsfw || !db.LibraryRoots.Any(root => root.Id == holding.LibraryRootId && root.IsNsfw)
            where after == null || groupRank > after.GroupRank
                || groupRank == after.GroupRank && (sortAt < after.SortAt
                    || sortAt == after.SortAt && (HoldingRank > after.TypeRank
                        || HoldingRank == after.TypeRank && holding.Id.ToString().CompareTo(after.Id.ToString()) < 0))
            orderby groupRank, sortAt descending, holding.Id.ToString() descending
            select new TimedHolding(holding, occurredAt, sortAt, groupRank);
        return query.Take(limit);
    }

    private async Task<IReadOnlyList<RequestActivityItem>> MapAsync(
        IReadOnlyList<Candidate> candidates,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var holdingIds = candidates.Where(candidate => candidate.HoldingRow is not null)
            .Select(candidate => candidate.Id).ToArray();
        var bindings = holdingIds.Length == 0
            ? []
            : await db.ManagedSourceBindings.AsNoTracking()
                .Where(binding => holdingIds.Contains(binding.HoldingId))
                .ToArrayAsync(cancellationToken);
        var mapped = candidates.Select(candidate => Map(candidate, bindings)).ToArray();
        if (!hideNsfw) return mapped;

        var libraryRootIds = mapped.Select(item => item.Transfer?.LibraryRootId
                ?? item.Request?.LibraryRootId
                ?? item.Holding?.LibraryRootId)
            .OfType<Guid>().Distinct().ToArray();
        var hiddenRootIds = await db.LibraryRoots.AsNoTracking()
            .Where(root => libraryRootIds.Contains(root.Id) && root.IsNsfw)
            .Select(root => root.Id)
            .ToHashSetAsync(cancellationToken);
        var entityIds = mapped.SelectMany(item => item.Transfer?.ImportedEntityIds ?? [])
            .Concat(mapped.Select(item => item.Request?.EntityId).OfType<Guid>())
            .Concat(mapped.SelectMany(item => item.Holding?.Targets.Select(target => target.EntityId) ?? []))
            .Distinct().ToArray();
        var hiddenIds = entityIds.Length == 0
            ? []
            : await db.Entities.AsNoTracking()
                .Where(entity => entityIds.Contains(entity.Id) && entity.IsNsfw)
                .Select(entity => entity.Id)
                .ToHashSetAsync(cancellationToken);
        return mapped.Where(item => !hiddenRootIds.Contains(item.Transfer?.LibraryRootId
                ?? item.Request?.LibraryRootId
                ?? item.Holding?.LibraryRootId
                ?? Guid.Empty)
            && !(item.Transfer?.ImportedEntityIds.Any(hiddenIds.Contains) ?? false)
            && !(item.Request is { } request && hiddenIds.Contains(request.EntityId))
            && !(item.Holding?.Targets.Any(target => hiddenIds.Contains(target.EntityId)) ?? false)).ToArray();
    }

    private RequestActivityItem Map(Candidate candidate, IReadOnlyList<ManagedSourceBindingRow> bindings) {
        if (candidate.TransferRow is { } transfer) {
            var state = JsonSerializer.Deserialize<IntegrationTransferState>(transfer.StateJson, Json)
                ?? throw new InvalidDataException("Stored integration transfer state is invalid.");
            var plan = JsonSerializer.Deserialize<IntegrationTransferPlan>(
                transferPlans.Unprotect(transfer.ConnectionId, transfer.Id, transfer.ProtectedPlan), Json)
                ?? throw new InvalidDataException("Stored integration transfer intent is invalid.");
            var work = new StoredIntegrationTransfer(
                new IntegrationTransfer(state), plan, transfer.CreatedAt, transfer.UpdatedAt, transfer.LastError);
            var transferResponse = new IntegrationTransferResponse(
                state.OperationId, state.ConnectionId, plan.Title, plan.EntityKind, plan.LibraryRootId,
                state.Mode, state.Phase, transfer.CreatedAt, transfer.UpdatedAt, state.Artifacts?.Count ?? 0,
                state.Imports?.SelectMany(imported => imported.ContainerEntityId is { } container
                    ? new[] { container }
                    : imported.EntityIds).Distinct().ToArray() ?? [],
                transfer.LastError, work.Transfer.CanCancelSource || work.Transfer.CanCancelRemote,
                state.CancellationRequested, plan.Source?.Publication, state.LastSourceState,
                state.SourceProgress, state.SourceProblem);
            return new(transfer.Id, transfer.ConnectionId, candidate.OccurredAt, Transfer: transferResponse);
        }
        if (candidate.RequestRow is { } request) {
            var state = JsonSerializer.Deserialize<ManagedRequestState>(request.StateJson, Json)
                ?? throw new InvalidDataException("Stored managed request state is invalid.");
            var plan = JsonSerializer.Deserialize<ManagedRequestPlan>(request.PlanJson, Json)
                ?? throw new InvalidDataException("Stored managed request intent is invalid.");
            var work = new StoredManagedRequest(
                new ManagedRequestOperation(state), plan, request.CreatedAt, request.UpdatedAt, request.Problem);
            var requestResponse = new ManagedRequestResponse(
                state.OperationId, state.ConnectionId, state.EntityId, state.LibraryRootId, plan.DisplayTitle(), state.Phase,
                state.Revision, state.RemoteId, plan.Request.Monitored, plan.Request.Search, state.ReviewRequired,
                work.Operation.CanCancel, request.CreatedAt, request.UpdatedAt, request.Problem,
                plan.ExistingHoldingId ?? state.OperationId,
                plan.Request.TargetEntityIds);
            return new(request.Id, request.ConnectionId, candidate.OccurredAt, Request: requestResponse);
        }
        var holding = candidate.HoldingRow
            ?? throw new InvalidDataException("Activity candidate has no retained payload.");
        var holdingBindings = bindings.Where(binding => binding.HoldingId == holding.Id)
            .GroupBy(binding => binding.RemoteFileId, StringComparer.Ordinal)
            .Select(group => {
                var file = group.First();
                return new ManagedFileBinding(file.RemoteFileId, file.LocalPath, file.SizeBytes, file.WrittenAt,
                    file.IsAvailable, group.Select(binding => new ManagedEntityBinding(
                        new(binding.RemoteTargetId, binding.Kind, binding.SeasonNumber, binding.EpisodeNumber, binding.AbsoluteNumber, binding.IssueLabel),
                        binding.EntityId, binding.SourceFileId)).ToArray());
            }).ToArray();
        var holdingResponse = new ManagedTrackingResponse(
            holding.Id, holding.ConnectionId, holding.LibraryRootId,
            JsonSerializer.Deserialize<ManagedItemInput>(holding.ItemJson, Json)
                ?? throw new InvalidDataException("Stored managed holding item is invalid."),
            holding.Title, holding.Status, holding.Revision, holding.LastCheckedAt, holding.Problem, holdingBindings,
            JsonSerializer.Deserialize<ManagedTargetBinding[]>(holding.TargetsJson, Json)
                ?? throw new InvalidDataException("Stored managed holding targets are invalid."),
            holding.ReleaseOperationId, holding.ReleasedAt);
        return new(holding.Id, holding.ConnectionId, candidate.OccurredAt, Holding: holdingResponse);
    }

    private async Task<IReadOnlyList<RequestActivitySource>> SourceHealthAsync(
        Guid? connectionId,
        CancellationToken cancellationToken) =>
        await db.IntegrationConnections.AsNoTracking()
            .Where(connection => connectionId == null || connection.Id == connectionId)
            .Where(connection => db.IntegrationTransfers.Any(transfer => transfer.ConnectionId == connection.Id)
                || db.ManagedRequests.Any(request => request.ConnectionId == connection.Id)
                || db.ManagedHoldings.Any(holding => holding.ConnectionId == connection.Id))
            .OrderBy(connection => connection.Name)
            .ThenBy(connection => connection.Id)
            .Select(connection => new RequestActivitySource(
                connection.Id,
                connection.Name,
                connection.Status,
                connection.Status != ConnectionStatus.Ready || connection.LastCheckedAt == null,
                connection.LastCheckedAt,
                connection.LastError))
            .ToArrayAsync(cancellationToken);

    private sealed record TimedTransfer(IntegrationTransferRow Row, DateTimeOffset OccurredAt, DateTimeOffset SortAt, int GroupRank);
    private sealed record TimedRequest(ManagedRequestRow Row, DateTimeOffset OccurredAt, DateTimeOffset SortAt, int GroupRank);
    private sealed record TimedHolding(ManagedHoldingRow Row, DateTimeOffset OccurredAt, DateTimeOffset SortAt, int GroupRank);

    private sealed record Candidate(
        Guid Id,
        int TypeRank,
        int GroupRank,
        DateTimeOffset OccurredAt,
        DateTimeOffset SortAt,
        IntegrationTransferRow? TransferRow,
        ManagedRequestRow? RequestRow,
        ManagedHoldingRow? HoldingRow) {
        public static Candidate Transfer(TimedTransfer candidate) =>
            new(candidate.Row.Id, TransferRank, candidate.GroupRank, candidate.OccurredAt, candidate.SortAt, candidate.Row, null, null);
        public static Candidate Request(TimedRequest candidate) =>
            new(candidate.Row.Id, RequestRank, candidate.GroupRank, candidate.OccurredAt, candidate.SortAt, null, candidate.Row, null);
        public static Candidate Holding(TimedHolding candidate) =>
            new(candidate.Row.Id, HoldingRank, candidate.GroupRank, candidate.OccurredAt, candidate.SortAt, null, null, candidate.Row);
    }

    private sealed record ActivityCursor(DateTimeOffset SortAt, int GroupRank, int TypeRank, Guid Id) {
        public static string Encode(DateTimeOffset sortAt, int groupRank, int typeRank, Guid id, Guid? connectionId, bool hideNsfw) {
            var value = $"{sortAt.UtcTicks}:{groupRank}:{typeRank}:{id:N}:{connectionId?.ToString("N") ?? "-"}:{(hideNsfw ? 1 : 0)}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static ActivityCursor? Decode(string? value, Guid? connectionId, bool hideNsfw) {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value.Length > 256) throw new ArgumentException("The activity cursor is invalid.");
            try {
                var encoded = value.Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split(':');
                if (parts.Length != 6 || !long.TryParse(parts[0], out var ticks)
                    || !int.TryParse(parts[1], out var groupRank) || groupRank is < AttentionGroupRank or > RecentGroupRank
                    || !int.TryParse(parts[2], out var typeRank) || typeRank is < TransferRank or > HoldingRank
                    || !Guid.TryParseExact(parts[3], "N", out var id)
                    || parts[4] != (connectionId?.ToString("N") ?? "-")
                    || parts[5] != (hideNsfw ? "1" : "0")) throw new FormatException();
                return new(new DateTimeOffset(ticks, TimeSpan.Zero), groupRank, typeRank, id);
            } catch (Exception error) when (error is FormatException or ArgumentOutOfRangeException) {
                throw new ArgumentException("The activity cursor is invalid.");
            }
        }
    }
}
