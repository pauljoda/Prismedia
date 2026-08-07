using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Resolves the effective transfer preference against enabled download-client capabilities and applies
/// it consistently after candidates cross persistence boundaries. A sole enabled protocol always wins;
/// a configured preference is meaningful only when both protocols are available.
/// </summary>
public static class AcquisitionProtocolPreference {
    /// <summary>Returns the effective preferred protocol, or null when no download protocol is enabled.</summary>
    public static async Task<DownloadProtocol?> ResolveAsync(
        IDownloadClientConfigStore downloadClients,
        SettingsService settings,
        CancellationToken cancellationToken) {
        var enabled = (await downloadClients.GetEnabledProtocolsAsync(cancellationToken)).Distinct().ToArray();
        if (enabled.Length == 0) {
            return null;
        }
        if (enabled.Length == 1) {
            return enabled[0];
        }

        var configured = (await settings.GetPreferredDownloadProtocolSettingsAsync(cancellationToken)).Protocol;
        return enabled.Contains(configured) ? configured : enabled[0];
    }

    /// <summary>
    /// Orders accepted candidates by their complete quality/profile score, using effective protocol
    /// preference only as a tie-break. This mirrors the Arr decision order: a preferred transport never
    /// turns a lower-quality release into the better release.
    /// </summary>
    public static IOrderedEnumerable<T> Order<T>(
        IEnumerable<T> candidates,
        DownloadProtocol? preferredProtocol,
        Func<T, DownloadProtocol> protocol,
        Func<T, double> score,
        Func<T, double>? swarmTieBreak = null) {
        swarmTieBreak ??= static _ => 0;
        return candidates
            .OrderByDescending(candidate => score(candidate) - swarmTieBreak(candidate))
            .ThenByDescending(candidate => preferredProtocol is not null && protocol(candidate) == preferredProtocol)
            .ThenByDescending(score);
    }
}

/// <summary>Shared extraction of the final swarm-health component embedded in release scores.</summary>
public static class AcquisitionReleaseRanking {
    /// <summary>
    /// Returns only the torrent seed/peer tie-break portion of a score. Removing it temporarily lets
    /// quality, revision and profile preferences compare first, protocol second, and swarm health last.
    /// </summary>
    public static double SwarmTieBreak(
        EntityKind kind,
        DownloadProtocol protocol,
        int? seeders,
        int? peers) {
        if (protocol != DownloadProtocol.Torrent) {
            return 0;
        }

        if (kind == EntityKind.Book) {
            return (Math.Log10(Math.Max(seeders ?? 0, 0) + 1) * 100)
                + (Math.Min(Math.Max(peers ?? 0, 0), 100) * 0.25);
        }

        return Math.Min(Math.Max(seeders ?? 0, 0), 9_999);
    }
}
