using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobQueueService {
    private static (JobGraphRow Graph, JobRunRow Run) CreateRootGraph(
        EnqueueJobRequest request,
        JobGraphOrigin origin,
        Guid? initiatingUserId,
        DateTimeOffset now) {
        var graphId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var graph = new JobGraphRow {
            Id = graphId,
            Origin = origin,
            Status = JobGraphStatus.Queued,
            DisplayName = request.TargetLabel ?? request.Type.ToCode(),
            RootRunId = runId,
            InitiatingUserId = initiatingUserId,
            RootEntityKind = request.GraphRootEntityKind ?? request.TargetEntityKind,
            RootEntityId = request.GraphRootEntityId ?? request.TargetEntityId,
            ActiveKey = ActiveGraphKeyFor(request, origin),
            CreatedAt = now,
            UpdatedAt = now
        };
        var row = new JobRunRow {
            Id = runId,
            GraphId = graphId,
            NodeKey = request.NodeKey ?? "root",
            Importance = request.Importance ?? JobDefinitionRegistry.Importance(request.Type),
            ResourceClass = request.ResourceClass ?? JobDefinitionRegistry.ResourceClass(request.Type),
            ResourceKey = request.ResourceKey ?? EntityResourceKey(request),
            Sequence = 0,
            Type = request.Type,
            Status = JobRunStatus.Queued,
            PayloadJson = request.PayloadJson ?? "{}",
            Attempts = 0,
            MaxAttempts = 3,
            Progress = 0,
            TargetEntityKind = request.TargetEntityKind,
            TargetEntityId = request.TargetEntityId,
            TargetLabel = request.TargetLabel,
            AvailableAt = now,
            CreatedAt = now
        };
        return (graph, row);
    }

    private static JobGraphOrigin OriginFor(EnqueueJobRequest request) => request.Origin;

    private Guid? InitiatingUserId() =>
        _currentUser is { IsAuthenticated: true, IsSystem: false } current
            && current.UserId != Guid.Empty
                ? current.UserId
                : null;

    private static string? ActiveGraphKeyFor(EnqueueJobRequest request, JobGraphOrigin origin) {
        if (origin != JobGraphOrigin.Background) return null;
        if (request.TargetEntityId is not null) {
            return $"{request.Type.ToCode()}:{request.TargetEntityId}";
        }

        return JobDefinitionRegistry.IsQueueWideSingleton(request.Type, hasTarget: false)
            ? request.Type.ToCode()
            : null;
    }

    private static string DefaultNodeKey(EnqueueJobRequest request) {
        var target = request.TargetEntityId;
        if (target is null) {
            var payload = request.PayloadJson ?? "{}";
            target = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16].ToLowerInvariant();
        }

        return $"{request.Type.ToCode()}:{target}";
    }

    private static string? EntityResourceKey(EnqueueJobRequest request) =>
        JobResourceDeclaration.EntityKey(request);

    private async Task EnsureEntityResourceDeclaredAsync(
        string? resourceKey,
        CancellationToken cancellationToken) {
        await JobResourceDeclaration.EnsureImplicitAsync(_db, resourceKey, cancellationToken);
    }

    internal static JobRunSnapshot ToSnapshot(JobRunRow row, JobGraphOrigin? graphOrigin = null) {
        return new JobRunSnapshot(
            row.Id,
            row.Type,
            row.Status,
            row.Progress,
            row.Message,
            row.PayloadJson,
            row.TargetEntityKind,
            row.TargetEntityId,
            row.TargetLabel,
            row.CreatedAt,
            row.StartedAt,
            row.FinishedAt,
            row.Attempts,
            row.MaxAttempts,
            row.GraphId,
            graphOrigin,
            row.NodeKey,
            row.ParentRunId,
            row.Importance,
            row.ResourceClass,
            row.ResourceKey);
    }
}
