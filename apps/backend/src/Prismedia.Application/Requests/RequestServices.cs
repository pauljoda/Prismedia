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
}

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
        var results = new List<RequestSearchResult>();
        var errors = new List<RequestProviderHealth>();

        foreach (var instance in instances.Where(instance => sourceFilter is null || sourceFilter.Contains(instance.Kind))) {
            try {
                var found = await clients.Get(instance.Kind).SearchAsync(instance, request.Query, cancellationToken);
                results.AddRange(request.Kinds.Count == 0
                    ? found
                    : found.Where(result => request.Kinds.Contains(result.Kind)));
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                errors.Add(new RequestProviderHealth(instance.Id, instance.Kind, instance.DisplayName, ex.Message));
            }
        }

        return new RequestSearchResponse(results, errors);
    }
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

/// <summary>Loads service-level request provider options and health checks.</summary>
public sealed class RequestServiceOptionsService(IRequestServiceInstanceStore store, IRequestProviderClientFactory clients) {
    public async Task<RequestServiceOptionsResponse?> GetOptionsAsync(Guid serviceId, CancellationToken cancellationToken) {
        var instance = await store.GetAsync(serviceId, cancellationToken);
        return instance is null
            ? null
            : await clients.Get(instance.Kind).GetOptionsAsync(instance, cancellationToken);
    }

    public async Task<RequestConnectionTestResponse?> TestAsync(Guid serviceId, CancellationToken cancellationToken) {
        var instance = await store.GetAsync(serviceId, cancellationToken);
        return instance is null
            ? null
            : await clients.Get(instance.Kind).TestAsync(instance, cancellationToken);
    }
}

/// <summary>Submits selected request options to the chosen upstream service instance.</summary>
public sealed class RequestSubmitService(IRequestServiceInstanceStore store, IRequestProviderClientFactory clients) {
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
        return await client.SubmitAsync(instance, detail, request, cancellationToken);
    }
}
