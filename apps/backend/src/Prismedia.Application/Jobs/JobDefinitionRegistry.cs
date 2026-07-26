using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>Central scheduling defaults for the closed set of durable job types.</summary>
public static class JobDefinitionRegistry {
    private static readonly HashSet<JobType> HeavyCpuJobs = [
        JobType.GeneratePreview,
        JobType.GenerateAudioWaveform,
        JobType.ExtractSubtitles,
        JobType.GenerateBookPageThumbnail
    ];

    private static readonly HashSet<JobType> StandardCpuJobs = [
        JobType.ProbeVideo,
        JobType.ProbeAudio,
        JobType.FingerprintVideo,
        JobType.FingerprintImage,
        JobType.FingerprintAudio,
        JobType.GenerateImageThumbnail,
        JobType.GenerateGridThumbnail,
        JobType.GenerateBookCoverThumbnail
    ];

    private static readonly HashSet<JobType> BestEffortJobs = [
        JobType.FingerprintVideo,
        JobType.FingerprintImage,
        JobType.FingerprintAudio,
        JobType.GeneratePreview,
        JobType.GenerateImageThumbnail,
        JobType.GenerateGridThumbnail,
        JobType.GenerateBookPageThumbnail,
        JobType.GenerateBookCoverThumbnail,
        JobType.GenerateAudioWaveform,
        JobType.AcquireSubtitles,
        JobType.AutoIdentify
    ];

    /// <summary>Returns the job type's default CPU cost class.</summary>
    public static JobResourceClass ResourceClass(JobType type) =>
        HeavyCpuJobs.Contains(type)
            ? JobResourceClass.HeavyCpu
            : StandardCpuJobs.Contains(type)
                ? JobResourceClass.StandardCpu
                : JobResourceClass.Light;

    /// <summary>Returns whether failure should block required graph completion.</summary>
    public static JobNodeImportance Importance(JobType type) =>
        BestEffortJobs.Contains(type)
            ? JobNodeImportance.BestEffort
            : JobNodeImportance.Required;
}
