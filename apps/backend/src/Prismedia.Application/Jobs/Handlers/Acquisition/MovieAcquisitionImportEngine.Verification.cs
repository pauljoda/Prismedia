using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

public sealed partial class MovieAcquisitionImportEngine {
    private async Task<bool> VerifyVideoFilesAsync(JobContext context, Guid acquisitionId,
        IEnumerable<string> paths, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(30, "Verifying complete video and audio", cancellationToken);
        foreach (var path in paths.Distinct(FileSystemPathComparison.Comparer)) {
            if (await videoVerifier.FindFailureAsync(path, cancellationToken) is not { } failure) continue;
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.ManualImportRequired,
                failure, cancellationToken);
            return false;
        }
        return true;
    }
}
