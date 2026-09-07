using Prismedia.Application.Files;

namespace Prismedia.Application.Acquisition;

/// <summary>A complete decode result with process-local evidence for the exact file that passed.</summary>
public sealed record VideoPayloadVerification(VerifiedVideoPayload? Verified, string? FailureReason);

/// <summary>
/// Evidence created only by a successful full decode of an unchanged file. It allows placement to
/// recheck that file inside a short catalog transaction without decoding again while scans are blocked.
/// This evidence is never accepted from an API request or persisted across process restarts.
/// </summary>
public sealed class VerifiedVideoPayload {
    private readonly string path;
    private readonly long length;
    private readonly DateTime modified;

    private VerifiedVideoPayload(string path, long length, DateTime modified) {
        this.path = path;
        this.length = length;
        this.modified = modified;
    }

    /// <summary>Checks that the intended file still has the exact path and facts observed during decoding.</summary>
    public bool Matches(string candidatePath) {
        var file = new FileInfo(candidatePath);
        return FileSystemPathComparison.Equals(path, Path.GetFullPath(candidatePath))
            && file.Exists && file.Length == length && file.LastWriteTimeUtc == modified;
    }

    /// <summary>Fully decodes a stable file; unavailable or changed input returns a review explanation.</summary>
    public static async Task<VideoPayloadVerification> VerifyAsync(
        IVideoPayloadVerifier verifier, string filePath, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        try {
            var file = new FileInfo(filePath);
            if (!file.Exists || file.Length <= 0)
                return new(null, "The downloaded video is missing or empty; verification could not start.");
            var evidence = new VerifiedVideoPayload(file.FullName, file.Length, file.LastWriteTimeUtc);
            if (await verifier.FindFailureAsync(file.FullName, cancellationToken) is { } failure)
                return new(null, failure);
            return evidence.Matches(file.FullName)
                ? new(evidence, null)
                : new(null, "The downloaded video changed during verification. Both copies were retained for review.");
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return new(null, "The downloaded video became unavailable during verification. Both copies were retained for review.");
        }
    }
}
