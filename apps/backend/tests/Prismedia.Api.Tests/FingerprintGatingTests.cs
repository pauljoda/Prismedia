using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class FingerprintGatingTests {
    private static LibrarySettingsData Settings(bool oshash, bool md5) => new(
        AutoGenerateMetadata: false,
        AutoGenerateOshash: oshash,
        AutoGenerateMd5: md5,
        GeneratePhash: false,
        AutoGeneratePreview: false,
        GenerateTrickplay: false,
        TrickplayIntervalSeconds: 10,
        PreviewClipDurationSeconds: 8,
        ThumbnailQuality: 2,
        TrickplayQuality: 2);

    private static DownstreamNeeds Needs(bool missingOshash, bool missingMd5) => new(
        NeedsProbe: false,
        MissingOshash: missingOshash,
        MissingMd5: missingMd5,
        NeedsPreview: false,
        NeedsTrickplay: false,
        NeedsSubtitleExtraction: false,    NeedsGridThumbnail: false);

    [Theory]
    // oshash enabled and missing → fingerprint
    [InlineData(true, false, true, false, true)]
    // oshash enabled but already present, md5 disabled → nothing to do
    [InlineData(true, false, false, true, false)]
    // md5 enabled and missing → fingerprint even though oshash is present
    [InlineData(false, true, false, true, true)]
    // both algorithms disabled → never fingerprint, even when everything is missing
    [InlineData(false, false, true, true, false)]
    public void ShouldFingerprint_RespectsEnabledAlgorithmsAndMissingState(
        bool oshashEnabled, bool md5Enabled, bool missingOshash, bool missingMd5, bool expected) {
        var result = FingerprintGating.ShouldFingerprint(
            Settings(oshashEnabled, md5Enabled),
            Needs(missingOshash, missingMd5));

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ShouldFingerprintAsync_OnlyQueriesEnabledAlgorithms() {
        var persistence = new FakeFingerprintPresence(hasOshash: false, hasMd5: false);

        // MD5 disabled: a missing oshash should still trigger work, and MD5 must not be queried.
        var result = await FingerprintGating.ShouldFingerprintAsync(
            persistence, Settings(oshash: true, md5: false), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result);
        Assert.Contains(FingerprintAlgorithm.Oshash, persistence.QueriedAlgorithms);
        Assert.DoesNotContain(FingerprintAlgorithm.Md5, persistence.QueriedAlgorithms);
    }

    [Fact]
    public async Task ShouldFingerprintAsync_ReturnsFalseWhenAllEnabledAlgorithmsPresent() {
        var persistence = new FakeFingerprintPresence(hasOshash: true, hasMd5: true);

        var result = await FingerprintGating.ShouldFingerprintAsync(
            persistence, Settings(oshash: true, md5: true), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    private sealed class FakeFingerprintPresence(bool hasOshash, bool hasMd5) : IDownstreamNeedsPersistence {
        public List<FingerprintAlgorithm> QueriedAlgorithms { get; } = [];

        public Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken) {
            QueriedAlgorithms.Add(algorithm);
            return Task.FromResult(algorithm == FingerprintAlgorithm.Oshash ? hasOshash : hasMd5);
        }

        public Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
