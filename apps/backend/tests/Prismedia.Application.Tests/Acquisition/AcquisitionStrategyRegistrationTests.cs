using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>Guards the definition-family acquisition strategy convention and factory routing.</summary>
public sealed class AcquisitionStrategyRegistrationTests {
    [Fact]
    public void DiscoveredStrategiesCoverEveryRequestAcquisitionKindExactlyOnce() {
        AcquisitionStrategyRegistration.ValidateCoverage();
    }

    [Fact]
    public void SearchPolicyRegistryResolvesEveryRequestAcquisitionKindFromItsDefinitionFamily() {
        var registry = new AcquisitionPolicyRegistry([
            new BookAcquisitionPolicyModule(),
            new MovieAcquisitionPolicyModule(),
            new MusicAcquisitionPolicyModule(),
            new TvAcquisitionPolicyModule()
        ]);

        foreach (var kind in RequestKindRegistry.All
                     .Select(descriptor => descriptor.AcquisitionKind)
                     .Distinct()) {
            Assert.NotNull(registry.Get(kind));
        }
    }

    [Fact]
    public void ImportFactoryResolvesEveryRequestAcquisitionKindByProfileNamingFamily() {
        var book = new BookEngine();
        var movie = new MovieEngine();
        var music = new MusicEngine();
        var television = new TelevisionEngine();
        var factory = new AcquisitionImportEngineFactory([book, movie, music, television]);

        Assert.Same(book, factory.Find(EntityKind.Book));
        Assert.Same(movie, factory.Find(EntityKind.Movie));
        Assert.Same(music, factory.Find(EntityKind.AudioLibrary));
        Assert.Same(music, factory.Find(EntityKind.AudioTrack));
        Assert.Same(television, factory.Find(EntityKind.VideoSeason));
        Assert.Same(television, factory.Find(EntityKind.VideoEpisode));
        Assert.Null(factory.Find(EntityKind.Video));
    }

    [Fact]
    public void ImportFactoryRejectsDuplicateFamilyCoverage() {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new AcquisitionImportEngineFactory([
                new BookEngine(), new DuplicateBookEngine(), new MovieEngine(), new MusicEngine(), new TelevisionEngine()
            ]));

        Assert.Contains(EntityKind.Book.ToCode(), error.Message, StringComparison.Ordinal);
        Assert.Contains(AcquisitionNamingFamily.Book.ToCode(), error.Message, StringComparison.Ordinal);
    }

    private abstract class TestEngine : IAcquisitionImportEngine {
        public Task ImportAsync(
            JobContext context,
            AcquisitionImportContext import,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [AcquisitionStrategy(AcquisitionNamingFamily.Book)]
    private sealed class BookEngine : TestEngine;

    [AcquisitionStrategy(AcquisitionNamingFamily.Book)]
    private sealed class DuplicateBookEngine : TestEngine;

    [AcquisitionStrategy(AcquisitionNamingFamily.Movie)]
    private sealed class MovieEngine : TestEngine;

    [AcquisitionStrategy(AcquisitionNamingFamily.Music)]
    private sealed class MusicEngine : TestEngine;

    [AcquisitionStrategy(AcquisitionNamingFamily.Television)]
    private sealed class TelevisionEngine : TestEngine;
}
