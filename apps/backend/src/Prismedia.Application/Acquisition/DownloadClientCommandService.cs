using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;

namespace Prismedia.Application.Acquisition;

/// <summary>Application use case for listing, saving, deleting, and testing download client configurations.</summary>
public sealed class DownloadClientCommandService(
    IDownloadClientConfigStore store,
    IDownloadClientFactory clients) {
    public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveRequest request, CancellationToken cancellationToken) =>
        store.SaveAsync(ToCommand(request), cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    /// <summary>Resolves the secrets (reusing stored ones when none are supplied for an existing client), then probes connectivity.</summary>
    public async Task<DownloadClientTestResponse> TestAsync(DownloadClientTestRequest request, CancellationToken cancellationToken) {
        ValidateBaseUrl(request.BaseUrl);

        var password = request.Password;
        var apiKey = request.ApiKey;
        if ((string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(apiKey)) && request.Id is { } id) {
            var stored = await store.GetAsync(id, cancellationToken);
            password = string.IsNullOrWhiteSpace(password) ? stored?.Password : password;
            apiKey = string.IsNullOrWhiteSpace(apiKey) ? stored?.ApiKey : apiKey;
        }

        var connection = new DownloadClientConnection(
            request.Id ?? Guid.Empty, request.Kind, request.BaseUrl.Trim().TrimEnd('/'), request.Username, password, string.Empty, apiKey);
        var result = await clients.Get(request.Kind).TestAsync(connection, cancellationToken);
        return new DownloadClientTestResponse(result.Connected, result.Message);
    }

    private static DownloadClientSaveCommand ToCommand(DownloadClientSaveRequest request) {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientInvalid, "A display name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Category)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientInvalid, "A category/label is required.");
        }

        ValidateBaseUrl(request.BaseUrl);

        return new DownloadClientSaveCommand(
            request.Id,
            request.Kind,
            request.DisplayName.Trim(),
            request.BaseUrl.Trim().TrimEnd('/'),
            string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            request.Password,
            request.Category.Trim(),
            request.Enabled,
            request.ApiKey,
            request.Priority,
            request.SeedRatio,
            request.SeedTimeMinutes);
    }

    private static void ValidateBaseUrl(string baseUrl) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientInvalid, "The base URL must be an absolute http or https URL.");
        }
    }
}
