using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Collects a complete sealed manifest revision without allowing pagination to change the accepted output set.</summary>
public sealed class IntegrationManifestReader(IIntegrationTransferGateway gateway) {
    #region Actions - Reading

    /// <summary>Reads bounded pages for one exact job/revision, then applies domain validation to the complete manifest.</summary>
    public async Task<IntegrationArtifactManifest> ReadAsync(string pluginId, IntegrationConnectionContext connection,
        string jobId, string revision, CancellationToken cancellationToken) {
        const int pageLimit = 100;
        var artifacts = new List<IntegrationArtifact>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        int? count = null;
        do {
            var page = await gateway.ReadManifestAsync(pluginId, connection, new(jobId, revision, cursor, pageLimit), cancellationToken);
            if (page.JobId != jobId || page.Revision != revision || !page.Sealed
                || page.ArtifactCount is < 1 or > IntegrationArtifactManifest.MaximumArtifacts
                || page.Artifacts is null || page.Artifacts.Count is < 1 or > pageLimit || count is not null && count != page.ArtifactCount
                || page.NextCursor?.Length > 8192 || artifacts.Count + page.Artifacts.Count > page.ArtifactCount) {
                throw new IntegrationInvocationException("The executor returned an incomplete or inconsistent artifact manifest page.");
            }

            count = page.ArtifactCount;
            artifacts.AddRange(page.Artifacts);
            cursor = string.IsNullOrEmpty(page.NextCursor) ? null : page.NextCursor;
            if (cursor is not null && !cursors.Add(cursor)) {
                throw new IntegrationInvocationException("The artifact manifest repeated a continuation cursor.");
            }
        } while (cursor is not null);

        try {
            return new(jobId, revision, true, count!.Value, artifacts);
        } catch (ArgumentException) {
            throw new IntegrationInvocationException("The sealed output manifest contains invalid or incomplete artifact evidence.");
        }
    }

    #endregion
}
