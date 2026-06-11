using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>New request history record captured when a submit is accepted upstream.</summary>
public sealed record RequestHistoryAddRequest(
    Guid ServiceInstanceId,
    string ServiceName,
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    string? Subtitle,
    int? Year,
    string? PosterUrl,
    string? UpstreamId,
    bool Monitored,
    IReadOnlyList<string> SelectedChildIds);

/// <summary>Status refresh for a request history entry after a live upstream check.</summary>
public sealed record RequestHistoryStatusUpdate(Guid Id, RequestHistoryStatus Status, string? StatusMessage, string? UpstreamId);

/// <summary>Persistence port for submitted request history.</summary>
public interface IRequestHistoryStore {
    Task AddAsync(RequestHistoryAddRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<RequestHistoryEntry>> ListAsync(int limit, CancellationToken cancellationToken);
    Task UpdateStatusesAsync(IReadOnlyList<RequestHistoryStatusUpdate> updates, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Lists submitted request history with statuses refreshed live from each upstream service.
/// Refreshed statuses are written back to the store so the last known state survives outages;
/// unreachable services surface as provider warnings and their entries keep the cached status.
/// </summary>
public sealed class RequestHistoryService(
    IRequestHistoryStore history,
    IRequestServiceInstanceStore store,
    IRequestProviderClientFactory clients) {
    private const int HistoryLimit = 200;

    public async Task<RequestHistoryResponse> ListAsync(CancellationToken cancellationToken) {
        var entries = await history.ListAsync(HistoryLimit, cancellationToken);
        if (entries.Count == 0) {
            return new RequestHistoryResponse([], []);
        }

        var instances = (await store.ListDetailsAsync(cancellationToken)).ToDictionary(instance => instance.Id);
        var groups = entries
            .Where(entry => entry.ServiceId is { } id && instances.ContainsKey(id))
            .GroupBy(entry => entry.ServiceId!.Value)
            .Select(group => (Instance: instances[group.Key], Entries: group.ToArray()))
            .ToArray();

        var refreshes = await Task.WhenAll(groups.Select(group => RefreshInstanceAsync(group.Instance, group.Entries, cancellationToken)));

        var statusById = new Dictionary<Guid, RequestStatusResult>();
        var errors = new List<RequestProviderHealth>();
        foreach (var (statuses, error) in refreshes) {
            foreach (var status in statuses) {
                statusById[status.HistoryId] = status;
            }
            if (error is not null) {
                errors.Add(error);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var updates = new List<RequestHistoryStatusUpdate>();
        var refreshed = entries.Select(entry => {
            if (!statusById.TryGetValue(entry.Id, out var status)) {
                return entry;
            }

            var upstreamId = status.UpstreamId ?? entry.UpstreamId;
            if (status.Status != entry.Status || status.Message != entry.StatusMessage || upstreamId != entry.UpstreamId) {
                updates.Add(new RequestHistoryStatusUpdate(entry.Id, status.Status, status.Message, upstreamId));
            }

            return entry with { Status = status.Status, StatusMessage = status.Message, UpstreamId = upstreamId, StatusUpdatedAt = now };
        }).ToArray();

        if (updates.Count > 0) {
            await history.UpdateStatusesAsync(updates, cancellationToken);
        }

        return new RequestHistoryResponse(refreshed, errors);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        history.DeleteAsync(id, cancellationToken);

    private async Task<(IReadOnlyList<RequestStatusResult> Statuses, RequestProviderHealth? Error)> RefreshInstanceAsync(
        RequestServiceInstanceDetail instance,
        IReadOnlyList<RequestHistoryEntry> entries,
        CancellationToken cancellationToken) {
        try {
            var probes = entries.Select(entry => new RequestStatusProbe(entry.Id, entry.Kind, entry.ExternalId, entry.UpstreamId)).ToArray();
            var statuses = await clients.Get(instance.Kind).GetStatusesAsync(instance, probes, cancellationToken);
            return (statuses, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return ([], new RequestProviderHealth(instance.Id, instance.Kind, instance.DisplayName, ex.Message));
        }
    }
}
