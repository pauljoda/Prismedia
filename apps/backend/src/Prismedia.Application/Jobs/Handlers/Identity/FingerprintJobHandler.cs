using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Identity;

/// <summary>
/// Computes the fingerprints enabled in generation settings (oshash and/or MD5) for an entity's
/// source file and stores them as entity file fingerprint records. oshash is cheap (head + tail
/// read); MD5 streams the whole file, so it is only computed when explicitly enabled. Registered
/// once per media type via factory DI.
/// </summary>
public sealed class FingerprintJobHandler(
    JobType jobType,
    ILogger<FingerprintJobHandler> logger,
    IMediaHashing hashing,
    ILibraryScanRootPersistence roots,
    IMediaProcessingStatePersistence persistence) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => jobType;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        var settings = await roots.GetSettingsAsync(cancellationToken);
        if (!settings.AutoGenerateOshash && !settings.AutoGenerateMd5) {
            await context.ReportProgressAsync(100, "Fingerprinting disabled", cancellationToken);
            return;
        }

        var timer = new JobPhaseTimer();
        await context.ReportProgressAsync(10, "Computing hashes", cancellationToken);

        FileHashData hashes;
        using (timer.Phase("hash")) {
            hashes = await hashing.ComputeHashesAsync(filePath, settings.AutoGenerateMd5, cancellationToken);
        }

        Guid? sourceFileId;
        using (timer.Phase("persist")) {
            sourceFileId = await Persistence.GetSourceFileIdAsync(entityId, cancellationToken);
            if (settings.AutoGenerateOshash) {
                await Persistence.UpsertEntityFingerprintAsync(entityId, FingerprintAlgorithm.Oshash, hashes.Oshash, sourceFileId, cancellationToken);
            }
            if (settings.AutoGenerateMd5 && hashes.Md5 is not null) {
                await Persistence.UpsertEntityFingerprintAsync(entityId, FingerprintAlgorithm.Md5, hashes.Md5, sourceFileId, cancellationToken);
            }
        }

        var report = timer.Finish();
        logger.LogInformation(
            "[METRICS] {JobType} {Label} — oshash={Oshash} md5={Md5} — {Timing}",
            Type.ToCode(), context.Job.TargetLabel, hashes.Oshash,
            hashes.Md5 is null ? "skipped" : hashes.Md5[..8], report.ToLogString());

        await context.ReportProgressAsync(100, "Fingerprint complete", cancellationToken);
    }
}
