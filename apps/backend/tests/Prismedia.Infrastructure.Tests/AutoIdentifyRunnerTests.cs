using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class AutoIdentifyRunnerTests {
    [Fact]
    public async Task AppliesFirstConfidentProviderWithFullFieldsAndMarksOrganized() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1", "p2"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["p2"] = Proposal("p2", confidence: 0.95m, title: "The Matrix"),
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("p2", result.Provider);
        var applyCall = Assert.Single(identify.ApplyCalls);
        Assert.Contains("title", applyCall.Fields);
        Assert.Contains("images", applyCall.Fields);
        Assert.DoesNotContain("rating", applyCall.Fields);
        Assert.Equal("poster", Assert.Single(applyCall.SelectedImages!).Key);
        Assert.True((await db.Entities.SingleAsync()).IsOrganized);
    }

    [Fact]
    public async Task SkipsProvidersBelowConfidenceThreshold() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["p1"] = Proposal("p1", confidence: 0.5m, title: "Maybe Match"),
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Empty(identify.ApplyCalls);
        Assert.False((await db.Entities.SingleAsync()).IsOrganized);
    }

    [Fact]
    public async Task TreatsConfidenceFreeResultAsExactMatch() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["p1"] = Proposal("p1", confidence: null, title: "Exact Lookup"),
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task HydratesAndAppliesSingleConfidentSearchCandidate() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, kind: "video-series", title: "The Chair Company");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["tmdb"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["tmdb"] = CandidateShell("tmdb", "271267", "The Chair Company", confidence: 1m),
            },
            ProposalsByExternalId = {
                ["tmdb:271267"] = Proposal("tmdb", confidence: 1m, title: "The Chair Company", targetKind: ProposalKind.VideoSeries),
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("tmdb", result.Provider);
        Assert.Equal(2, identify.IdentifyCalls.Count);
        Assert.Equal("271267", identify.IdentifyCalls[1].Query?.ExternalIds?["tmdb"]);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task HydratesAndAppliesSingleExactUnscoredSearchCandidate() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(
            db,
            organized: false,
            kind: EntityKindRegistry.MusicArtist.Code,
            title: "NateWantsToBattle");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["musicbrainz"] = CandidateShell(
                    "musicbrainz",
                    "2918ce08-196e-408b-b3cb-4ccbefe62c3a",
                    "NateWantsToBattle",
                    confidence: null,
                    targetKind: ProposalKind.MusicArtist,
                    year: 1989),
            },
            ProposalsByExternalId = {
                ["musicbrainz:2918ce08-196e-408b-b3cb-4ccbefe62c3a"] = Proposal(
                    "musicbrainz",
                    confidence: 0.95m,
                    title: "NateWantsToBattle",
                    targetKind: ProposalKind.MusicArtist),
            },
            SupportedKindsByProvider = {
                ["musicbrainz"] = [EntityKindRegistry.MusicArtist.Code],
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal(2, identify.IdentifyCalls.Count);
        Assert.Equal("2918ce08-196e-408b-b3cb-4ccbefe62c3a", identify.IdentifyCalls[1].Query?.ExternalIds?["musicbrainz"]);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task HydratesYearSuffixedAlbumCandidateWhenProviderOmitsConfidence() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(
            db,
            organized: false,
            kind: EntityKindRegistry.AudioLibrary.Code,
            title: "Diddy Kong Racing Theme (2014)");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["musicbrainz"] = CandidateShell(
                    "musicbrainz",
                    "12e5490e-d1ab-4397-8830-dec3c4c74962",
                    "Diddy Kong Racing Theme",
                    confidence: null,
                    targetKind: ProposalKind.AudioLibrary,
                    year: 2014),
            },
            ProposalsByExternalId = {
                ["musicbrainz:12e5490e-d1ab-4397-8830-dec3c4c74962"] = Proposal(
                    "musicbrainz",
                    confidence: 0.9m,
                    title: "Diddy Kong Racing Theme",
                    targetKind: ProposalKind.AudioLibrary),
            },
            SupportedKindsByProvider = {
                ["musicbrainz"] = [EntityKindRegistry.AudioLibrary.Code],
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal(2, identify.IdentifyCalls.Count);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task DoesNotAutoHydrateAmbiguousUnscoredSearchCandidates() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(
            db,
            organized: false,
            kind: EntityKindRegistry.MusicArtist.Code,
            title: "Don McLean");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["musicbrainz"] = CandidateShells(
                    "musicbrainz",
                    ("fa19ee38-c2a9-4ed1-9b24-a18100cf9db3", "Don McLean", (decimal?)null, 1945),
                    ("18836d7a-2c12-4d62-9d6e-d25776d2887c", "Don McLean", (decimal?)null, null)),
            },
            SupportedKindsByProvider = {
                ["musicbrainz"] = [EntityKindRegistry.MusicArtist.Code],
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Single(identify.IdentifyCalls);
        Assert.Empty(identify.ApplyCalls);
    }

    [Fact]
    public async Task AllowsLongSeriesIdentifyWhenCascadeKeepsMakingProgress() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, kind: "video-series", title: "Long Series");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["tmdb"], confidencePercent: 90m);
        var progressReports = new List<AutoIdentifyProgress>();
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["tmdb"] = Proposal("tmdb", confidence: 1m, title: "Long Series", targetKind: ProposalKind.VideoSeries),
            },
            OnIdentifyAsync = async (_, providerId, _, sink, cancellationToken) => {
                Assert.Equal("tmdb", providerId);
                Assert.NotNull(sink);
                for (var i = 0; i < 3; i++) {
                    await Task.Delay(60, cancellationToken);
                    var partial = Proposal("tmdb", confidence: 1m, title: "Long Series", targetKind: ProposalKind.VideoSeries) with {
                        Children = Enumerable.Range(0, i + 1)
                            .Select(index => Proposal("tmdb", confidence: 1m, title: $"Episode {index + 1}", targetKind: ProposalKind.Video))
                            .ToArray()
                    };
                    await sink!.OnEntityResolvedAsync(partial, cancellationToken);
                }

                return new IdentifyPluginResponse(
                    true,
                    Proposal("tmdb", confidence: 1m, title: "Long Series", targetKind: ProposalKind.VideoSeries),
                    null);
            }
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(
            entityId,
            new AutoIdentifyRunOptions(
                TimeSpan.FromMilliseconds(100),
                (progress, _) => {
                    progressReports.Add(progress);
                    return Task.CompletedTask;
                }),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("tmdb", result.Provider);
        Assert.Equal([1, 2, 3], progressReports.Select(progress => progress.ResolvedSteps));
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task AllowsLongSeriesIdentifyWhenNestedCascadeReportsProgressHeartbeats() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, kind: "video-series", title: "Nested Series");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["tmdb"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["tmdb"] = Proposal("tmdb", confidence: 1m, title: "Nested Series", targetKind: ProposalKind.VideoSeries),
            },
            OnIdentifyAsync = async (_, _, _, sink, cancellationToken) => {
                Assert.NotNull(sink);
                for (var i = 0; i < 3; i++) {
                    await Task.Delay(60, cancellationToken);
                    await sink!.OnProgressAsync(cancellationToken);
                }

                return new IdentifyPluginResponse(
                    true,
                    Proposal("tmdb", confidence: 1m, title: "Nested Series", targetKind: ProposalKind.VideoSeries),
                    null);
            }
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(
            entityId,
            new AutoIdentifyRunOptions(TimeSpan.FromMilliseconds(100)),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task AllowsLongApplyWhenEachAppliedEntityReportsProgressHeartbeats() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, kind: "video-series", title: "King of the Hill");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["tmdb"], confidencePercent: 90m);
        var progressReports = new List<AutoIdentifyProgress>();
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["tmdb"] = Proposal("tmdb", confidence: 1m, title: "King of the Hill", targetKind: ProposalKind.VideoSeries),
            },
            OnApplyAsync = async (_, _, _, _, progress, cancellationToken) => {
                Assert.NotNull(progress);
                for (var i = 0; i < 3; i++) {
                    await Task.Delay(60, cancellationToken);
                    await progress!.ReportEntityAsync(
                        EntityKind.VideoSeason,
                        $"Season {i + 1}",
                        ["King of the Hill", $"Season {i + 1}"],
                        cancellationToken);
                }

                return true;
            }
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(
            entityId,
            new AutoIdentifyRunOptions(
                TimeSpan.FromMilliseconds(100),
                (progress, _) => {
                    progressReports.Add(progress);
                    return Task.CompletedTask;
                }),
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Single(identify.ApplyCalls);
        Assert.Contains(progressReports, progress =>
            progress.Phase == AutoIdentifyProgressPhase.Applying &&
            progress.CurrentTitle == "Season 3");
    }

    [Fact]
    public async Task CancelsProviderWhenNoCascadeProgressArrivesBeforeInactivityTimeout() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, kind: "video-series", title: "Stalled Series");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["tmdb"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["tmdb"] = Proposal("tmdb", confidence: 1m, title: "Stalled Series", targetKind: ProposalKind.VideoSeries),
            },
            OnIdentifyAsync = async (_, _, _, _, cancellationToken) => {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                return new IdentifyPluginResponse(
                    true,
                    Proposal("tmdb", confidence: 1m, title: "Stalled Series", targetKind: ProposalKind.VideoSeries),
                    null);
            }
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            entityId,
            new AutoIdentifyRunOptions(TimeSpan.FromMilliseconds(40)),
            CancellationToken.None));

        Assert.Empty(identify.ApplyCalls);
    }

    [Fact]
    public async Task LeavesSearchCandidatesForReviewWhenCandidateConfidenceIsBelowThreshold() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, kind: "video-series", title: "The Chair Company");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["tmdb"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["tmdb"] = CandidateShell("tmdb", "271267", "The Chair Company", confidence: 0.5m),
            },
            ProposalsByExternalId = {
                ["tmdb:271267"] = Proposal("tmdb", confidence: 1m, title: "The Chair Company", targetKind: ProposalKind.VideoSeries),
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Single(identify.IdentifyCalls);
        Assert.Empty(identify.ApplyCalls);
    }

    [Fact]
    public async Task SkipsOrganizedEntityWhenUnorganizedOnly() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: true);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Anything") },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("already organized", result.SkipReason);
        Assert.Empty(identify.IdentifyCalls);
    }

    [Fact]
    public async Task SkipsOrganizedEntityBeforeWaitingForProviderSlot() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: true);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Anything") },
        };
        var gate = new AutoIdentifyConcurrencyGate();
        using var heldSlot = gate.TryEnterBackground();
        Assert.NotNull(heldSlot);
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance,
            gate);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("already organized", result.SkipReason);
        Assert.Empty(identify.IdentifyCalls);
    }

    [Fact]
    public async Task RequeuesWhenProviderSlotIsBusyAfterCheapSkipsPass() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Anything") },
        };
        var gate = new AutoIdentifyConcurrencyGate();
        using var heldSlot = gate.TryEnterBackground();
        Assert.NotNull(heldSlot);
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance,
            gate);

        var retry = await Assert.ThrowsAsync<JobRetryLaterException>(() => runner.RunAsync(entityId, CancellationToken.None));

        Assert.Equal("Auto identify provider slot busy.", retry.Message);
        Assert.Equal(TimeSpan.FromSeconds(5), retry.RetryDelay);
        Assert.Empty(identify.IdentifyCalls);
    }

    [Fact]
    public async Task AppliesSeriesRootWhoseRootPatchHasNullCollections() {
        await using var db = CreateContext();
        var seriesId = await SeedVideoAsync(db, organized: false, kind: "video-series");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        // A series root often arrives with a sparse patch (null collections) and its value in Children.
        var sparsePatch = new EntityMetadataPatch(
            Title: "The Chair Company",
            Description: null,
            ExternalIds: null!,
            Urls: null!,
            Tags: null!,
            Studio: null,
            Credits: null!,
            Dates: null!,
            Stats: null!,
            Positions: null!,
            Classification: null);
        var proposal = new EntityMetadataProposal(
            ProposalId: Guid.NewGuid().ToString(),
            Provider: "p1",
            TargetKind: ProposalKind.VideoSeries,
            Confidence: null,
            MatchReason: null,
            Patch: sparsePatch,
            Images: null!,
            Children: [],
            Candidates: [],
            Relationships: null!);
        var identify = new FakeIdentifyProvider { ProposalsByProvider = { ["p1"] = proposal } };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(seriesId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Contains("title", Assert.Single(identify.ApplyCalls).Fields);
    }

    [Fact]
    public async Task SkipsChildEntitiesSoOnlyTheParentIsIdentified() {
        await using var db = CreateContext();
        var seriesId = await SeedVideoAsync(db, organized: false);
        var episodeId = await SeedVideoAsync(db, organized: false, parentId: seriesId);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Episode") },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(episodeId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("child entity; its parent is identified instead", result.SkipReason);
        Assert.Empty(identify.IdentifyCalls);
    }

    [Fact]
    public async Task SkipsWantedTargetsWithoutQueryingProvidersOrConsumingAnAttempt() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(
            db,
            organized: false,
            isWanted: true,
            attachSource: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Anything") },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("wanted entity; source media is not on disk", result.SkipReason);
        Assert.Empty(identify.IdentifyCalls);
        Assert.Equal(0, (await db.Entities.SingleAsync()).AutoIdentifyAttempts);
    }

    [Fact]
    public async Task SkipsFilelessTargetsWithoutQueryingProvidersOrConsumingAnAttempt() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, attachSource: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Anything") },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("no source media on disk", result.SkipReason);
        Assert.Empty(identify.IdentifyCalls);
        Assert.Equal(0, (await db.Entities.SingleAsync()).AutoIdentifyAttempts);
    }

    [Fact]
    public async Task MatchesProviderCapabilityByConcreteKindSoAlbumsAutoIdentify() {
        await using var db = CreateContext();
        var albumId = await SeedVideoAsync(db, organized: false, kind: EntityKindRegistry.AudioLibrary.Code, title: "Abbey Road");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["musicbrainz"] = Proposal("musicbrainz", confidence: 0.95m, title: "Abbey Road", targetKind: ProposalKind.AudioLibrary),
            },
            // Mirrors the MusicBrainz manifest: concrete kinds only, no generic "audio" kind, so a
            // capability lookup by the settings selector kind would wrongly exclude the provider.
            SupportedKindsByProvider = {
                ["musicbrainz"] = [
                    EntityKindRegistry.MusicArtist.Code,
                    EntityKindRegistry.AudioLibrary.Code,
                    EntityKindRegistry.AudioTrack.Code,
                ],
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(albumId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("musicbrainz", result.Provider);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task AppliesArtistParentedAudioAlbumBecauseAlbumIsScanRoot() {
        await using var db = CreateContext();
        var artistId = await SeedVideoAsync(
            db,
            organized: false,
            kind: EntityKindRegistry.MusicArtist.Code,
            title: "The Beatles");
        var albumId = await SeedVideoAsync(
            db,
            organized: false,
            parentId: artistId,
            kind: EntityKindRegistry.AudioLibrary.Code,
            title: "Abbey Road");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["musicbrainz"] = Proposal("musicbrainz", confidence: 0.95m, title: "Abbey Road", targetKind: ProposalKind.AudioLibrary),
            },
            SupportedKindsByProvider = {
                ["musicbrainz"] = [
                    EntityKindRegistry.MusicArtist.Code,
                    EntityKindRegistry.AudioLibrary.Code,
                    EntityKindRegistry.AudioTrack.Code,
                ],
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(albumId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("musicbrainz", result.Provider);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task PassesIdentifiedArtistExternalIdsWhenAutoIdentifyingParentedAlbum() {
        await using var db = CreateContext();
        var artistId = await SeedVideoAsync(
            db,
            organized: true,
            kind: EntityKindRegistry.MusicArtist.Code,
            title: "The Beatles");
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = artistId,
            Provider = "musicbrainz",
            Value = "artist-mbid",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var albumId = await SeedVideoAsync(
            db,
            organized: false,
            parentId: artistId,
            kind: EntityKindRegistry.AudioLibrary.Code,
            title: "Abbey Road");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["musicbrainz"] = Proposal("musicbrainz", confidence: 0.95m, title: "Abbey Road", targetKind: ProposalKind.AudioLibrary),
            },
            SupportedKindsByProvider = {
                ["musicbrainz"] = [EntityKindRegistry.AudioLibrary.Code],
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(albumId, CancellationToken.None);

        Assert.True(result.Applied);
        var externalIds = Assert.Single(identify.ParentExternalIdsCalls);
        Assert.NotNull(externalIds);
        Assert.Equal("artist-mbid", externalIds!["musicbrainz"]);
    }

    [Fact]
    public async Task AppliesMusicArtistWithoutCascadingIntoItsAlbumRoots() {
        await using var db = CreateContext();
        var artistId = await SeedVideoAsync(
            db,
            organized: false,
            kind: EntityKindRegistry.MusicArtist.Code,
            title: "The Beatles");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["musicbrainz"] = Proposal("musicbrainz", confidence: 0.95m, title: "The Beatles", targetKind: ProposalKind.MusicArtist),
            },
            SupportedKindsByProvider = {
                ["musicbrainz"] = [
                    EntityKindRegistry.MusicArtist.Code,
                    EntityKindRegistry.AudioLibrary.Code,
                    EntityKindRegistry.AudioTrack.Code,
                ],
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(artistId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("musicbrainz", result.Provider);
        Assert.Single(identify.ApplyCalls);
        // Albums under the artist are independent auto-identify roots; the artist identify must
        // not re-enumerate them as cascading children.
        Assert.All(identify.CascadeChildrenCalls, cascade => Assert.False(cascade));
    }

    [Fact]
    public async Task MarksAppliedProposalTreeOrganizedBeforeAutoApply() {
        await using var db = CreateContext();
        var albumId = await SeedVideoAsync(db, organized: false, kind: EntityKindRegistry.AudioLibrary.Code, title: "What You Want (2020)");
        var trackId = Guid.NewGuid();
        var proposal = Proposal("musicbrainz", confidence: 0.95m, title: "What You Want", targetKind: ProposalKind.AudioLibrary) with {
            TargetEntityId = albumId,
            Children = [
                Proposal("musicbrainz", confidence: 0.95m, title: "Introduction", targetKind: ProposalKind.AudioTrack) with {
                    TargetEntityId = trackId,
                    Images = []
                }
            ]
        };
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["musicbrainz"] = proposal },
            SupportedKindsByProvider = { ["musicbrainz"] = [EntityKindRegistry.AudioLibrary.Code] },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(albumId, CancellationToken.None);

        Assert.True(result.Applied);
        var applied = Assert.Single(identify.AppliedProposals);
        Assert.True(applied.Patch.Flags?.IsOrganized);
        var child = Assert.Single(applied.Children);
        Assert.True(child.Patch.Flags?.IsOrganized);
        Assert.Contains("title", Assert.Single(identify.ApplyCalls).Fields);
    }

    [Fact]
    public async Task ThrowsRetryLaterWhenProviderReportsRateLimit() {
        await using var db = CreateContext();
        var albumId = await SeedVideoAsync(db, organized: false, kind: EntityKindRegistry.AudioLibrary.Code, title: "Abbey Road");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ErrorsByProvider = {
                ["musicbrainz"] = "429 Too Many Requests"
            },
            SupportedKindsByProvider = {
                ["musicbrainz"] = [EntityKindRegistry.AudioLibrary.Code],
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var retry = await Assert.ThrowsAsync<JobRetryLaterException>(() => runner.RunAsync(albumId, CancellationToken.None));

        Assert.Equal("Auto identify provider musicbrainz is temporarily unavailable: 429 Too Many Requests", retry.Message);
        Assert.Equal(TimeSpan.FromMinutes(1), retry.RetryDelay);
        Assert.Empty(identify.ApplyCalls);
        Assert.False((await db.Entities.SingleAsync()).IsOrganized);
    }

    [Fact]
    public async Task ConsumesOneAttemptWhenProvidersWereQueriedWithoutAConfidentMatch() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["p1"] = Proposal("p1", confidence: 0.5m, title: "Maybe Match"),
            },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal(1, (await db.Entities.SingleAsync()).AutoIdentifyAttempts);
        Assert.Contains("no confident match", result.SkipReason);
    }

    [Fact]
    public async Task SkipsEntityWithoutQueryingProvidersOnceAttemptsAreExhausted() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, autoIdentifyAttempts: AutoIdentifyPolicy.MaxAttemptsPerEntity);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Anything") },
        };
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("auto identify attempts exhausted; identify manually", result.SkipReason);
        Assert.Empty(identify.IdentifyCalls);
    }

    [Fact]
    public async Task DoesNotConsumeAnAttemptWhenNoProviderIsCapable() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider();
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("no capable provider", result.SkipReason);
        Assert.Equal(0, (await db.Entities.SingleAsync()).AutoIdentifyAttempts);
    }

    [Fact]
    public async Task SkipsWhenDisabled() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: false, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider();
        var runner = new AutoIdentifyRunner(
            settings,
            identify,
            db,
            new EfIdentifyTargetEligibilityService(db),
            NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Empty(identify.IdentifyCalls);
    }

    private static EntityMetadataProposal Proposal(string provider, decimal? confidence, string title, ProposalKind targetKind = ProposalKind.Video) =>
        new(
            ProposalId: Guid.NewGuid().ToString(),
            Provider: provider,
            TargetKind: targetKind,
            Confidence: confidence,
            MatchReason: null,
            Patch: new EntityMetadataPatch(
                Title: title,
                Description: "A film.",
                ExternalIds: new Dictionary<string, string> { ["tmdb"] = "603" },
                Urls: [],
                Tags: [],
                Studio: null,
                Credits: [],
                Dates: new Dictionary<string, string>(),
                Stats: new Dictionary<string, int>(),
                Positions: new Dictionary<string, int>(),
                Classification: null) {
                Rating = 4
            },
            Images: [new ImageCandidate("poster", "https://img/poster.jpg", provider, 1m, null, null, null)],
            Children: [],
            Candidates: [],
            Relationships: []);

    private static EntityMetadataProposal CandidateShell(
        string provider,
        string externalId,
        string title,
        decimal? confidence,
        ProposalKind targetKind = ProposalKind.VideoSeries,
        int? year = 2025) =>
        CandidateShells(provider, [(externalId, title, confidence, year)], targetKind);

    private static EntityMetadataProposal CandidateShells(
        string provider,
        params (string ExternalId, string Title, decimal? Confidence, int? Year)[] candidates) =>
        CandidateShells(provider, candidates, ProposalKind.VideoSeries);

    private static EntityMetadataProposal CandidateShells(
        string provider,
        (string ExternalId, string Title, decimal? Confidence, int? Year)[] candidates,
        ProposalKind targetKind) =>
        new(
            ProposalId: null!,
            Provider: provider,
            TargetKind: targetKind,
            Confidence: null,
            MatchReason: null,
            Patch: null!,
            Images: [],
            Children: [],
            Candidates: candidates
                .Select(candidate => new EntitySearchCandidate(
                    new Dictionary<string, string> { [provider] = candidate.ExternalId },
                    candidate.Title,
                    candidate.Year,
                    Overview: null,
                    PosterUrl: null,
                    Popularity: null,
                    CandidateId: $"{provider}:search:{candidate.ExternalId}",
                    Source: provider,
                    Confidence: candidate.Confidence,
                    MatchReason: candidate.Confidence is null ? null : "title-search"))
                .ToArray(),
            Relationships: []);

    private static async Task<Guid> SeedVideoAsync(
        PrismediaDbContext db,
        bool organized,
        Guid? parentId = null,
        string kind = "video",
        string title = "video.mkv",
        int autoIdentifyAttempts = 0,
        bool isWanted = false,
        bool attachSource = true) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            IsOrganized = organized,
            IsWanted = isWanted,
            ParentEntityId = parentId,
            AutoIdentifyAttempts = autoIdentifyAttempts,
            CreatedAt = now,
            UpdatedAt = now,
        });
        if (attachSource) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = id,
                Role = EntityFileRole.Source,
                Path = $"/media/{id}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<SettingsService> ConfigureAsync(
        PrismediaDbContext db,
        bool enabled,
        string[] providers,
        decimal confidencePercent) {
        var service = new SettingsService(new EfSettingsPersistence(db));
        await service.UpdateSettingsAsync(
            new Dictionary<string, JsonElement> {
                [AppSettingKeys.AutoIdentifyEnabled] = JsonSerializer.SerializeToElement(enabled),
                [AppSettingKeys.AutoIdentifyProviders] = JsonSerializer.SerializeToElement(providers),
                [AppSettingKeys.AutoIdentifyConfidenceThreshold] = JsonSerializer.SerializeToElement(confidencePercent),
            },
            CancellationToken.None);
        return service;
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"auto-identify-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }

    private sealed class FakeIdentifyProvider : IIdentifyProviderService {
        public Dictionary<string, EntityMetadataProposal> ProposalsByProvider { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> ErrorsByProvider { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, EntityMetadataProposal> ProposalsByExternalId { get; } = new(StringComparer.Ordinal);
        /// <summary>Optional manifest-style declared kinds per provider; providers absent here match any kind.</summary>
        public Dictionary<string, string[]> SupportedKindsByProvider { get; } = new(StringComparer.Ordinal);
        public List<(Guid EntityId, string Provider, IdentifyQuery? Query)> IdentifyCalls { get; } = [];
        public List<IReadOnlyDictionary<string, string>?> ParentExternalIdsCalls { get; } = [];
        public List<bool> CascadeChildrenCalls { get; } = [];
        public List<(IReadOnlyCollection<string> Fields, IReadOnlyDictionary<string, string?>? SelectedImages)> ApplyCalls { get; } = [];
        public List<EntityMetadataProposal> AppliedProposals { get; } = [];
        public Func<Guid, string, IdentifyQuery?, IIdentifyCascadeSink?, CancellationToken, Task<IdentifyPluginResponse>>? OnIdentifyAsync { get; init; }
        public Func<Guid, EntityMetadataProposal, IReadOnlyCollection<string>, IReadOnlyDictionary<string, string?>?, IIdentifyApplyProgressReporter?, CancellationToken, Task<bool>>? OnApplyAsync { get; init; }

        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken) {
            IReadOnlyList<PluginProvider> result = ProposalsByProvider.Keys
                .Concat(ErrorsByProvider.Keys)
                .Distinct(StringComparer.Ordinal)
                .Where(id => entityKind is null ||
                    !SupportedKindsByProvider.TryGetValue(id, out var kinds) ||
                    kinds.Contains(entityKind, StringComparer.OrdinalIgnoreCase))
                .Select(id => new PluginProvider(
                    Id: id,
                    Name: id,
                    Version: "1.0.0",
                    Installed: true,
                    Enabled: true,
                    IsNsfw: false,
                    Supports: [new PluginEntitySupport(entityKind ?? "video", ["search"])],
                    Auth: [],
                    MissingAuthKeys: []))
                .ToList();
            return Task.FromResult(result);
        }

        public async Task<IdentifyPluginResponse> IdentifyAsync(
            Guid entityId, string providerId, IdentifyQuery? query,
            IReadOnlyDictionary<string, string>? parentExternalIds, bool hideNsfw, CancellationToken cancellationToken,
            bool cascadeChildren = true, IIdentifyCascadeSink? sink = null, bool hydrateRelationships = true) {
            IdentifyCalls.Add((entityId, providerId, query));
            ParentExternalIdsCalls.Add(parentExternalIds);
            CascadeChildrenCalls.Add(cascadeChildren);
            if (OnIdentifyAsync is not null) {
                return await OnIdentifyAsync(entityId, providerId, query, sink, cancellationToken);
            }

            if (ErrorsByProvider.TryGetValue(providerId, out var error)) {
                return new IdentifyPluginResponse(false, null, error);
            }

            if (query?.ExternalIds is not null &&
                query.ExternalIds.TryGetValue(providerId, out var externalId) &&
                ProposalsByExternalId.TryGetValue($"{providerId}:{externalId}", out var lookupProposal)) {
                return new IdentifyPluginResponse(true, lookupProposal, null);
            }

            return ProposalsByProvider.TryGetValue(providerId, out var proposal)
                ? new IdentifyPluginResponse(true, proposal, null)
                : new IdentifyPluginResponse(false, null, "no result");
        }

        public Task<bool> ApplyAsync(
            Guid entityId,
            EntityMetadataProposal proposal,
            IReadOnlyCollection<string> selectedFields,
            IReadOnlyDictionary<string, string?>? selectedImages,
            CancellationToken cancellationToken,
            IIdentifyApplyProgressReporter? progress = null) {
            ApplyCalls.Add((selectedFields, selectedImages));
            AppliedProposals.Add(proposal);
            return OnApplyAsync is not null
                ? OnApplyAsync(entityId, proposal, selectedFields, selectedImages, progress, cancellationToken)
                : Task.FromResult(true);
        }
    }
}
