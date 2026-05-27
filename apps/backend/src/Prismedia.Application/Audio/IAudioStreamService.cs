namespace Prismedia.Application.Audio;

/// <summary>
/// Configuration port for audio transcoding tools used by stream planning.
/// </summary>
public interface IAudioTranscodeOptions {
    /// <summary>Absolute or PATH-resolved ffmpeg executable used for on-the-fly audio transcodes.</summary>
    string FfmpegPath { get; }
}

/// <summary>
/// Describes how the API should serve an audio stream without exposing infrastructure details.
/// </summary>
/// <param name="Path">Source file path.</param>
/// <param name="ContentType">Content type to serve for direct streams.</param>
/// <param name="DirectPlayable">Whether the source can be served directly with range support.</param>
/// <param name="Codec">Detected source codec, when available.</param>
/// <param name="FfmpegPath">ffmpeg executable path for transcode streams.</param>
public sealed record AudioStreamPlan(
    string Path,
    string ContentType,
    bool DirectPlayable,
    string? Codec,
    string FfmpegPath);

/// <summary>
/// Plans direct or transcoded audio streaming for an entity.
/// </summary>
public interface IAudioStreamService {
    /// <summary>Returns a stream plan for an audio entity, or null when no source exists.</summary>
    Task<AudioStreamPlan?> GetStreamAsync(Guid entityId, CancellationToken cancellationToken);
}

/// <summary>
/// Default application service that converts audio source metadata into a stream plan.
/// </summary>
public sealed class AudioStreamService(
    IAudioSourceService sources,
    IAudioTranscodeOptions transcodeOptions) : IAudioStreamService {
    public async Task<AudioStreamPlan?> GetStreamAsync(Guid entityId, CancellationToken cancellationToken) {
        var source = await sources.GetSourceAsync(entityId, cancellationToken);
        return source is null
            ? null
            : new AudioStreamPlan(
                source.Path,
                source.ContentType,
                source.DirectPlayable,
                source.Codec,
                transcodeOptions.FfmpegPath);
    }
}
