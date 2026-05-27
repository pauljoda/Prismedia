using Prismedia.Application.Audio;
using Prismedia.Infrastructure.Media.Processing;

namespace Prismedia.Infrastructure.Audio;

/// <summary>
/// Adapts configured media tool paths to the application audio streaming port.
/// </summary>
public sealed class MediaToolAudioTranscodeOptions(MediaToolOptions mediaTools) : IAudioTranscodeOptions {
    public string FfmpegPath => mediaTools.FfmpegPath;
}
