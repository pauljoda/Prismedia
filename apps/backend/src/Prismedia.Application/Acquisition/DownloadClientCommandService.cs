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

        // Testing a SAVED client reuses its stored secrets (the form never echoes them back) and its
        // stored category, so the test validates the category the client will actually add under. A
        // pre-save test carries no category and checks connectivity only.
        var stored = request.Id is { } id ? await store.GetAsync(id, cancellationToken) : null;
        var password = string.IsNullOrWhiteSpace(request.Password) ? stored?.Password : request.Password;
        var apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? stored?.ApiKey : request.ApiKey;

        var connection = new DownloadClientConnection(
            request.Id ?? Guid.Empty, request.Kind, request.BaseUrl.Trim().TrimEnd('/'), request.Username, password,
            stored?.Category ?? string.Empty, apiKey, request.DownloadDirectory ?? stored?.DownloadDirectory);
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

        if (request.Kind == Domain.Entities.DownloadClientKind.Slskd && string.IsNullOrWhiteSpace(request.DownloadDirectory)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientInvalid, "The slskd download directory is required.");
        }

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
            request.SeedTimeMinutes,
            string.IsNullOrWhiteSpace(request.DownloadDirectory) ? null : request.DownloadDirectory.Trim().TrimEnd('/', '\\'));
    }

    private static void ValidateBaseUrl(string baseUrl) {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientInvalid, "The base URL must be an absolute http or https URL.");
        }
    }
}
