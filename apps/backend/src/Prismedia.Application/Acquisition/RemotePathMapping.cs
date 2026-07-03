using Prismedia.Contracts.Acquisition;

namespace Prismedia.Application.Acquisition;

/// <summary>Persistence port for remote path mappings (client-reported path prefixes → Prismedia-local prefixes).</summary>
public interface IRemotePathMappingStore {
    Task<IReadOnlyList<RemotePathMappingView>> ListAsync(CancellationToken cancellationToken);
    Task<RemotePathMappingView> SaveAsync(RemotePathMappingSaveRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The mappings for one download client, longest remote prefix first.</summary>
    Task<IReadOnlyList<RemotePathMappingView>> ListForClientAsync(Guid downloadClientConfigId, CancellationToken cancellationToken);
}

/// <summary>
/// Rewrites paths a download client reports into paths Prismedia can read. A split-host or
/// containerized client sees its own filesystem (<c>/downloads/…</c>); the mapping translates that
/// prefix to where the same files live for Prismedia. Longest matching prefix wins; an unmapped path
/// passes through unchanged (the same-filesystem deployment needs no mappings at all).
/// </summary>
public sealed class RemotePathMapper(IRemotePathMappingStore mappings) {
    public async Task<string?> ToLocalAsync(Guid? downloadClientConfigId, string? clientPath, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(clientPath) || downloadClientConfigId is not { } clientId) {
            return clientPath;
        }

        foreach (var mapping in await mappings.ListForClientAsync(clientId, cancellationToken)) {
            if (TryRewrite(clientPath, mapping.RemotePath, mapping.LocalPath) is { } rewritten) {
                return rewritten;
            }
        }

        return clientPath;
    }

    /// <summary>
    /// Prefix rewrite honoring path-segment boundaries: <c>/downloads</c> matches <c>/downloads/x</c>
    /// and <c>/downloads</c> itself, but never <c>/downloads-archive</c>. Windows-style client paths
    /// (a qBittorrent on Windows reporting <c>C:\…</c>) compare case-insensitively on both slash kinds.
    /// </summary>
    private static string? TryRewrite(string path, string remotePrefix, string localPrefix) {
        var normalizedPrefix = remotePrefix.TrimEnd('/', '\\');
        if (normalizedPrefix.Length == 0 || !path.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var remainder = path[normalizedPrefix.Length..];
        if (remainder.Length > 0 && remainder[0] is not ('/' or '\\')) {
            return null;
        }

        var localRoot = localPrefix.TrimEnd('/', '\\');
        return remainder.Length == 0
            ? localRoot
            : localRoot + Path.DirectorySeparatorChar + remainder.TrimStart('/', '\\').Replace('\\', Path.DirectorySeparatorChar);
    }
}
