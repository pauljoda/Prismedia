using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

public sealed partial class MonitoredSearchJobHandler {
    private async Task<bool> TryScheduleOwnedInspectionAsync(DueMonitor monitor, JobContext context, CancellationToken cancellationToken) {
        if (monitor.EntityId is not { } entityId || monitor.Kind is not (EntityKind.Movie or EntityKind.VideoEpisode)
            || MonitoredSearchPayload.TryParse(context.Job.PayloadJson, out var payload) && payload.OwnedInspectionAttempted) return false;
        var scheduled = false;
        await monitors.ExecuteIfActiveEntityMutationAsync(entityId, async token => {
            if (await monitors.GetOwnedVideoInspectionNeedsAsync(monitor.MonitorId, token) is not { } needs) return;
            if (context.Job.GraphId is null) {
                // Legacy graphless jobs cannot express dependencies. Move this one monitor into a graph first.
                if (graphs is null) return;
                var root = new EnqueueJobRequest(JobType.MonitoredSearch,
                    PayloadJson: new MonitoredSearchPayload(monitor.MonitorId).ToJson(), TargetLabel: monitor.Title);
                await graphs.StartAsync(new StartJobGraphRequest(JobGraphOrigin.Background, $"Inspecting {monitor.Title}",
                    new GraphJobNodeRequest($"{JobType.MonitoredSearch.ToCode()}:{monitor.MonitorId}", root),
                    RootEntityKind: monitor.Kind.ToCode(), RootEntityId: entityId.ToString(),
                    ActiveKey: $"{JobType.ProbeVideo.ToCode()}:{monitor.MonitorId}"), token);
                scheduled = true;
                return;
            }
            var predecessor = context.Job.Id;
            foreach (var type in new[] { JobType.ProbeVideo, JobType.ExtractSubtitles }) {
                if (type == JobType.ProbeVideo && !needs.ProbeRequired
                    || type == JobType.ExtractSubtitles && !needs.SubtitlesRequired) continue;
                var node = await context.AppendNodeAsync(new GraphJobNodeRequest(
                    $"{context.Job.Id}:{type.ToCode()}:{entityId}",
                    EnqueueJobRequest.ForEntity(type, monitor.Kind, entityId.ToString(), monitor.Title),
                    DependsOn: [predecessor], ResourceClass: JobDefinitionRegistry.ResourceClass(type),
                    ResourceKey: JobResourceKeys.Entity(entityId.ToString())), token);
                predecessor = node.Id;
            }
            await context.AppendNodeAsync(new GraphJobNodeRequest(
                $"{context.Job.Id}:{JobType.MonitoredSearch.ToCode()}:{monitor.MonitorId}",
                new EnqueueJobRequest(JobType.MonitoredSearch,
                    PayloadJson: new MonitoredSearchPayload(monitor.MonitorId) { OwnedInspectionAttempted = true }.ToJson(),
                    TargetLabel: monitor.Title), DependsOn: [predecessor]), token);
            scheduled = true;
        }, cancellationToken);
        return scheduled;
    }
}
