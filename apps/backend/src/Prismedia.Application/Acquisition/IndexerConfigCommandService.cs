using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;

namespace Prismedia.Application.Acquisition;

/// <summary>Application use case for listing, saving, deleting, and testing indexer configurations.</summary>
public sealed class IndexerConfigCommandService(
    IIndexerConfigStore store,
    IIndexerSearchClientFactory clients) {
    public Task<IReadOnlyList<IndexerConfigSummary>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<IndexerConfigSummary> SaveAsync(IndexerConfigSaveRequest request, CancellationToken cancellationToken) =>
        store.SaveAsync(ToCommand(request), cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    /// <summary>Resolves the API key (reusing the stored key when none is supplied for an existing config), then probes connectivity.</summary>
    public async Task<IndexerTestResponse> TestAsync(IndexerTestRequest request, CancellationToken cancellationToken) {
        ValidateBaseUrl(request.BaseUrl);

        var apiKey = request.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) && request.Id is { } id) {
            apiKey = (await store.GetAsync(id, cancellationToken))?.ApiKey;
        }

        var connection = new IndexerConnection(request.Id ?? Guid.Empty, request.Kind, request.BaseUrl.Trim().TrimEnd('/'), apiKey, []);
        var result = await clients.Get(request.Kind).TestAsync(connection, cancellationToken);
        return new IndexerTestResponse(result.Connected, result.Message);
    }

    private static IndexerConfigSaveCommand ToCommand(IndexerConfigSaveRequest request) {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.IndexerInvalid, "A display name is required.");
        }

        ValidateBaseUrl(request.BaseUrl);

        return new IndexerConfigSaveCommand(
            request.Id,
            request.Kind,
            request.DisplayName.Trim(),
            request.BaseUrl.Trim().TrimEnd('/'),
            request.ApiKey,
            request.Enabled,
            request.Priority,
            request.Categories.Distinct().Order().ToArray(),
            request.QueryLimitPerHour,
            request.SeedRatio,
            request.SeedTimeMinutes);
    }

    private static void ValidateBaseUrl(string baseUrl) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.IndexerInvalid, "The base URL must be an absolute http or https URL.");
        }
    }
}
