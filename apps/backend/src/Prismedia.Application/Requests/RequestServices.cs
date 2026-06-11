using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Persistence port for configured request service instances.</summary>
public interface IRequestServiceInstanceStore {
    Task<IReadOnlyList<RequestServiceInstanceSummary>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RequestServiceInstanceDetail>> ListDetailsAsync(CancellationToken cancellationToken);
    Task<RequestServiceInstanceDetail?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<RequestServiceInstanceSummary> SaveAsync(RequestServiceInstanceSaveRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Provider adapter implemented by Radarr, Sonarr, Lidarr, and future plugin clients.</summary>
public interface IRequestProviderClient {
    RequestProviderKind Kind { get; }
    Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken);
    Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken);
    Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken);
    Task<RequestConnectionTestResponse> TestAsync(RequestServiceInstanceDetail instance, CancellationToken cancellationToken);
    Task<RequestServiceOptionsResponse> GetOptionsAsync(RequestServiceInstanceDetail instance, CancellationToken cancellationToken);

    /// <summary>Resolves the current upstream status for previously submitted requests in one batched pass.</summary>
    Task<IReadOnlyList<RequestStatusResult>> GetStatusesAsync(RequestServiceInstanceDetail instance, IReadOnlyList<RequestStatusProbe> probes, CancellationToken cancellationToken);
}

/// <summary>One history entry to refresh against an upstream service.</summary>
/// <param name="HistoryId">The request history row this probe belongs to.</param>
/// <param name="UpstreamId">Upstream library id captured at submit time, when known.</param>
public sealed record RequestStatusProbe(Guid HistoryId, RequestMediaKind Kind, string ExternalId, string? UpstreamId);

/// <summary>Live upstream status resolved for a single probe.</summary>
public sealed record RequestStatusResult(Guid HistoryId, RequestHistoryStatus Status, string? Message, string? UpstreamId);

/// <summary>Resolves the provider client for a request service kind.</summary>
public interface IRequestProviderClientFactory {
    IRequestProviderClient Get(RequestProviderKind kind);
}

/// <summary>Aggregates request searches across configured service instances.</summary>
public sealed class RequestSearchService(IRequestServiceInstanceStore store, IRequestProviderClientFactory clients) {
    public async Task<RequestSearchResponse> SearchAsync(RequestSearchRequest request, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.Query)) {
            return new RequestSearchResponse([], []);
        }

        var instances = await store.ListDetailsAsync(cancellationToken);
        var sourceFilter = request.Sources.Count == 0 ? null : request.Sources.ToHashSet();
        var targets = instances
            .Where(instance => sourceFilter is null || sourceFilter.Contains(instance.Kind))
            .Where(instance => CanServeRequestedKinds(instance.Kind, request.Kinds))
            .ToArray();

        var searches = await Task.WhenAll(targets.Select(instance => SearchInstanceAsync(instance, request, cancellationToken)));

        var results = new List<RequestSearchResult>();
        var errors = new List<RequestProviderHealth>();
        foreach (var (found, error) in searches) {
            results.AddRange(request.HideNsfw ? found.Where(result => !AdultCertifications.IsAdult(result.Certification)) : found);
            if (error is not null) {
                errors.Add(error);
            }
        }

        return new RequestSearchResponse(results, errors);
    }

    private async Task<(IReadOnlyList<RequestSearchResult> Results, RequestProviderHealth? Error)> SearchInstanceAsync(
        RequestServiceInstanceDetail instance,
        RequestSearchRequest request,
        CancellationToken cancellationToken) {
        try {
            var found = await clients.Get(instance.Kind).SearchAsync(instance, request.Query, cancellationToken);
            return (request.Kinds.Count == 0
                ? found
                : found.Where(result => request.Kinds.Contains(result.Kind)).ToArray(),
                null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return ([], new RequestProviderHealth(instance.Id, instance.Kind, instance.DisplayName, ex.Message));
        }
    }

    /// <summary>Skips upstream calls for providers that can never return the requested media kinds.</summary>
    private static bool CanServeRequestedKinds(RequestProviderKind provider, IReadOnlyList<RequestMediaKind> kinds) =>
        kinds.Count == 0 || kinds.Any(kind => provider switch {
            RequestProviderKind.Radarr => kind == RequestMediaKind.Movie,
            RequestProviderKind.Sonarr => kind == RequestMediaKind.Series,
            RequestProviderKind.Lidarr => kind is RequestMediaKind.Artist or RequestMediaKind.Album,
            _ => true
        });
}

/// <summary>Loads normalized detail metadata for an external request result.</summary>
public sealed class RequestDetailService(IRequestServiceInstanceStore store, IRequestProviderClientFactory clients) {
    public async Task<RequestDetailResponse?> GetAsync(RequestProviderKind source, RequestMediaKind kind, string externalId, Guid? serviceId, CancellationToken cancellationToken) {
        var instances = await store.ListDetailsAsync(cancellationToken);
        var instance = serviceId is { } id
            ? instances.FirstOrDefault(candidate => candidate.Id == id)
            : instances.Where(candidate => candidate.Kind == source)
                .OrderByDescending(candidate => candidate.IsDefault)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        if (instance is null) {
            return null;
        }
        if (instance.Kind != source) {
            return null;
        }

        var client = clients.Get(source);
        var detail = await client.GetDetailAsync(instance, kind, externalId, cancellationToken);
        var options = await client.GetOptionsAsync(instance, cancellationToken);
        return detail with { ServiceOptions = options };
    }
}

/// <summary>
/// Tests connectivity for a request service configuration that may not be saved yet and, on
/// success, pulls the selectable options (root folders, profiles, tags) from the service.
/// A successful test is the gate for saving a service in the settings flow.
/// </summary>
public sealed class RequestServiceTestService(IRequestServiceInstanceStore store, IRequestProviderClientFactory clients) {
    public async Task<RequestServiceTestResponse> TestAsync(RequestServiceTestRequest request, CancellationToken cancellationToken) {
        var apiKey = request.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) && request.Id is { } id) {
            var stored = await store.GetAsync(id, cancellationToken);
            apiKey = stored?.ApiKey;
        }

        var instance = new RequestServiceInstanceDetail(
            request.Id ?? Guid.Empty,
            request.Kind,
            string.Empty,
            request.BaseUrl,
            false,
            null,
            null,
            null,
            RequestMinimumAvailability.Released,
            [],
            true,
            !string.IsNullOrWhiteSpace(apiKey),
            apiKey);

        var client = clients.Get(request.Kind);
        var connection = await client.TestAsync(instance, cancellationToken);
        if (!connection.Connected) {
            return new RequestServiceTestResponse(false, connection.Message, null);
        }

        try {
            var options = await client.GetOptionsAsync(instance, cancellationToken);
            return new RequestServiceTestResponse(true, connection.Message, options);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new RequestServiceTestResponse(false, $"Connected, but loading options failed: {ex.Message}", null);
        }
    }
}

/// <summary>Submits selected request options to the chosen upstream service instance and records the request in history.</summary>
public sealed class RequestSubmitService(IRequestServiceInstanceStore store, IRequestProviderClientFactory clients, IRequestHistoryStore history) {
    public async Task<RequestSubmitResponse?> SubmitAsync(RequestSubmitRequest request, CancellationToken cancellationToken) {
        var instance = await store.GetAsync(request.ServiceId, cancellationToken);
        if (instance is null) {
            return null;
        }
        if (instance.Kind != request.Source) {
            return null;
        }

        var client = clients.Get(instance.Kind);
        var detail = await client.GetDetailAsync(instance, request.Kind, request.ExternalId, cancellationToken);
        var response = await client.SubmitAsync(instance, detail, request, cancellationToken);
        if (response.Submitted) {
            await history.AddAsync(new RequestHistoryAddRequest(
                instance.Id,
                instance.DisplayName,
                request.Source,
                request.Kind,
                request.ExternalId,
                detail.Title,
                detail.Subtitle,
                detail.Year,
                detail.PosterUrl,
                response.UpstreamId,
                request.Monitored,
                request.SelectedChildIds), cancellationToken);
        }

        return response;
    }
}
