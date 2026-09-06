namespace Prismedia.Application.Acquisition;

/// <summary>Verifies downloaded video and audio through full decoding before library placement or replacement.</summary>
public interface IVideoPayloadVerifier {
    /// <summary>Returns a review explanation when decoding cannot be verified; null means the complete file passed. Cancellation propagates.</summary>
    Task<string?> FindFailureAsync(string filePath, CancellationToken cancellationToken);
}
