using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class MediaUpgradePayloadInspectorTests {
    [Fact]
    public async Task ReadsMeasuredResolutionAndEmbeddedSubtitleFactsFromBothPayloads() {
        var root = Directory.CreateTempSubdirectory("prismedia-upgrade-inspection-");
        try {
            var owned = Directory.CreateDirectory(Path.Combine(root.FullName, "owned"));
            var candidate = Directory.CreateDirectory(Path.Combine(root.FullName, "candidate"));
            var ownedFile = Path.Combine(owned.FullName, "movie.mkv");
            var candidateFile = Path.Combine(candidate.FullName, "movie.mkv");
            await File.WriteAllBytesAsync(ownedFile, [1]);
            await File.WriteAllBytesAsync(candidateFile, [1]);
            var probe = new FakeMediaProbe(
                new Dictionary<string, VideoProbeData> {
                    [ownedFile] = Video(width: 3840, height: 1600),
                    [candidateFile] = Video(width: 1920, height: 800)
                },
                subtitleFiles: new HashSet<string> { candidateFile });
            var inspector = new MediaUpgradePayloadInspector(
                probe,
                NullLogger<MediaUpgradePayloadInspector>.Instance);

            var result = await inspector.InspectAsync(owned.FullName, candidate.FullName, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(2160, result.OwnedResolutionTier);
            Assert.Equal(1080, result.CandidateResolutionTier);
            Assert.False(result.OwnedHasEmbeddedSubtitles);
            Assert.True(result.CandidateHasEmbeddedSubtitles);
        } finally {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AmbiguousCandidatePayloadCannotBeInspectedAsAnAtomicUpgrade() {
        var root = Directory.CreateTempSubdirectory("prismedia-upgrade-inspection-");
        try {
            var owned = Directory.CreateDirectory(Path.Combine(root.FullName, "owned"));
            var candidate = Directory.CreateDirectory(Path.Combine(root.FullName, "candidate"));
            await File.WriteAllBytesAsync(Path.Combine(owned.FullName, "movie.mkv"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(candidate.FullName, "movie-a.mkv"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(candidate.FullName, "movie-b.mkv"), [1]);
            var inspector = new MediaUpgradePayloadInspector(
                new FakeMediaProbe(
                    new Dictionary<string, VideoProbeData>(),
                    new HashSet<string>()),
                NullLogger<MediaUpgradePayloadInspector>.Instance);

            Assert.Null(await inspector.InspectAsync(owned.FullName, candidate.FullName, CancellationToken.None));
        } finally {
            root.Delete(recursive: true);
        }
    }

    private static VideoProbeData Video(int width, int height) =>
        new(null, null, width, height, null, null, null, null, null, null, null);

    private sealed class FakeMediaProbe(
        IReadOnlyDictionary<string, VideoProbeData> videos,
        IReadOnlySet<string> subtitleFiles) : IMediaProbe {
        public Task<VideoProbeData?> ProbeVideoAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(videos.TryGetValue(filePath, out var video) ? video : null);

        public Task<IReadOnlyList<SubtitleStreamData>> ProbeSubtitleStreamsAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubtitleStreamData>>(subtitleFiles.Contains(filePath)
                ? [new SubtitleStreamData(2, "subrip", "eng", null)]
                : []);

        public Task<AudioProbeData?> ProbeAudioAsync(string filePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImageProbeData?> ProbeImageAsync(string filePath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
