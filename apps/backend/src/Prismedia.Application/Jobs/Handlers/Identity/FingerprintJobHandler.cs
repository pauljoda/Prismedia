using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Identity;

/// <summary>
/// Computes MD5 and oshash fingerprints for an entity's source file and stores them
/// as entity file fingerprint records. Registered once per media type via factory DI.
/// </summary>
public sealed class FingerprintJobHandler(
    JobType jobType,
    ILogger<FingerprintJobHandler> logger,
    IMediaHashing hashing,
    ILibraryScanPersistence persistence) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => jobType;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        var timer = new JobPhaseTimer();
        await context.ReportProgressAsync(10, "Computing hashes", cancellationToken);

        FileHashData hashes;
        using (timer.Phase("hash")) {
            hashes = await hashing.ComputeHashesAsync(filePath, cancellationToken);
        }

        Guid? sourceFileId;
        using (timer.Phase("persist")) {
            sourceFileId = await Persistence.GetSourceFileIdAsync(entityId, cancellationToken);
            await Persistence.UpsertEntityFingerprintAsync(entityId, FingerprintAlgorithm.Md5, hashes.Md5, sourceFileId, cancellationToken);
            await Persistence.UpsertEntityFingerprintAsync(entityId, FingerprintAlgorithm.Oshash, hashes.Oshash, sourceFileId, cancellationToken);
        }

        var report = timer.Finish();
        logger.LogInformation(
            "[METRICS] {JobType} {Label} — md5={Md5} — {Timing}",
            Type.ToCode(), context.Job.TargetLabel, hashes.Md5[..8], report.ToLogString());

        await context.ReportProgressAsync(100, "Fingerprint complete", cancellationToken);
    }
}
