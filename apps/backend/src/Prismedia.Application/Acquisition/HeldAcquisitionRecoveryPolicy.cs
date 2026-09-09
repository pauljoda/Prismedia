using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Patient, bounded retries after an initial acquisition needs review.</summary>
public static class HeldAcquisitionRecoveryPolicy {
    /// <summary>Excludes files already proven damaged from any claim of usable retained episode coverage.</summary>
    public static DownloadPayload? UsablePayload(DownloadPayload? payload, AcquisitionImportFileLedger? ledger) {
        if (payload is null) return null;
        var failed = ledger?.Files.Where(file => file.Decision == AcquisitionImportDecision.HoldVerification)
            .Select(file => file.SourceRelativePath).ToHashSet(FileSystemPathComparison.Comparer) ?? [];
        return payload with { Files = payload.Files.Where(file => !failed.Contains(file.RelativePath)).ToArray() };
    }

    /// <summary>Recovery starts no faster than six hours and backs off to one weekly search; it never expires.</summary>
    public static TimeSpan Delay(int intervalMinutes, int barrenSearches) => TimeSpan.FromMinutes(
        Math.Min(Math.Max(360, intervalMinutes) * Math.Pow(2, Math.Clamp(barrenSearches, 0, 10)), 7 * 24 * 60));
}

/// <summary>Creates independent automatic recovery work without resetting the retained download.</summary>
public interface IHeldAcquisitionAlternativeService {
    /// <summary>Records one completed recovery search, extending the interval only on an error-free miss.</summary>
    Task RecordSearchAsync(AcquisitionSearchInput input, AcquisitionSearchOutcome outcome, CancellationToken cancellationToken);

    /// <summary>Elects one new attempt only while the observed monitor and automatic hold still agree.</summary>
    Task<Guid?> CreateAsync(DueMonitor monitor, CancellationToken cancellationToken);
}
