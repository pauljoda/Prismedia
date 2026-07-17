using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class StructuralChildMatcherTests {
    [Fact]
    public void NormalEpisodeMatchStillUsesPositionWhenCountsAgree() {
        var local = Local(EntityKindRegistry.Video.Code, "Different Local Title", 1);
        var provider = Proposal(ProposalKind.VideoEpisode, "Magic Xylophone", ("episodeNumber", 1));

        var match = StructuralChildMatcher.FindProviderChild(local, [provider], new HashSet<int>(), cautious: false);

        Assert.Same(provider, match);
    }

    [Fact]
    public void CountMismatchDoesNotBindBlueyTheSignToProviderSurpriseByEpisodeNumber() {
        var localChildren = Enumerable.Range(1, 48)
            .Select(episode => Local(EntityKindRegistry.Video.Code, $"Episode {episode}", episode))
            .Concat([
                Local(EntityKindRegistry.Video.Code, "The Sign", 49),
                Local(EntityKindRegistry.Video.Code, "Surprise!", 50)
            ])
            .ToArray();
        var providerChildren = Enumerable.Range(1, 48)
            .Select(episode => Proposal(ProposalKind.VideoEpisode, $"Episode {episode}", ("episodeNumber", episode)))
            .Concat([
                Proposal(
                    ProposalKind.VideoEpisode,
                    "Surprise!",
                    ("episodeNumber", 49),
                    new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "4215673" })
            ])
            .ToArray();
        var used = new HashSet<int>();

        foreach (var local in localChildren.Take(48)) {
            Assert.NotNull(StructuralChildMatcher.FindProviderChild(local, providerChildren, used, cautious: true));
        }

        var theSign = StructuralChildMatcher.FindProviderChild(localChildren[48], providerChildren, used, cautious: true);
        var surprise = StructuralChildMatcher.FindProviderChild(localChildren[49], providerChildren, used, cautious: true);

        Assert.Null(theSign);
        Assert.NotNull(surprise);
        Assert.Equal("Surprise!", surprise.Patch.Title);
        Assert.Equal("4215673", surprise.Patch.ExternalIds[ExternalIdProviders.Tmdb]);
    }

    [Fact]
    public void CountMismatchStillBindsGenericSeasonSeriesTitleByNumber() {
        var localSeason = Local(EntityKindRegistry.VideoSeason.Code, "Season 3", 3);
        var providerSeason = Proposal(ProposalKind.VideoSeason, "Series 3", ("seasonNumber", 3));

        var match = StructuralChildMatcher.FindProviderChild(
            localSeason,
            [providerSeason],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerSeason, match);
    }

    [Fact]
    public void CountMismatchTreatsAmpersandAndAndAsEquivalentTitleTokens() {
        var localEpisode = Local(EntityKindRegistry.Video.Code, "Show and Tell", 42);
        var providerEpisode = Proposal(ProposalKind.VideoEpisode, "Show & Tell", ("episodeNumber", 42));

        var match = StructuralChildMatcher.FindProviderChild(
            localEpisode,
            [providerEpisode],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerEpisode, match);
    }

    [Fact]
    public void CountMismatchAllowsTinySpellingDifferenceWhenEpisodeNumberMatches() {
        var localEpisode = Local(EntityKindRegistry.Video.Code, "Safari, So Good!", 10);
        var providerEpisode = Proposal(ProposalKind.VideoEpisode, "Safari, So Goodie", ("episodeNumber", 10));

        var match = StructuralChildMatcher.FindProviderChild(
            localEpisode,
            [providerEpisode],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerEpisode, match);
    }

    [Fact]
    public void CountMismatchDoesNotBindAlbumTrackWhenNumberMatchesButTitleConflicts() {
        var localTrack = Local(EntityKindRegistry.AudioTrack.Code, "Local Hidden Track", 7);
        var providerTrack = Proposal(ProposalKind.AudioTrack, "Provider Bonus Track", ("trackNumber", 7));

        var match = StructuralChildMatcher.FindProviderChild(
            localTrack,
            [providerTrack],
            new HashSet<int>(),
            cautious: true);

        Assert.Null(match);
    }

    [Fact]
    public void CountMismatchStillBindsNumberMatchWhenTitlesAreCompatibleVariants() {
        var localTrack = Local(EntityKindRegistry.AudioTrack.Code, "Local Episode 1", 1);
        var providerTrack = Proposal(ProposalKind.AudioTrack, "Episode 1", ("trackNumber", 1));

        var match = StructuralChildMatcher.FindProviderChild(
            localTrack,
            [providerTrack],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerTrack, match);
    }

    [Fact]
    public void CountMismatchAllowsNumberMatchWhenLocalTitleIsOnlyGenericStructure() {
        var localEpisode = Local(EntityKindRegistry.Video.Code, "Episode 1", 1);
        var providerEpisode = Proposal(ProposalKind.VideoEpisode, "Magic Xylophone", ("episodeNumber", 1));

        var match = StructuralChildMatcher.FindProviderChild(
            localEpisode,
            [providerEpisode],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerEpisode, match);
    }

    [Fact]
    public void CountMismatchCanBindAlbumTrackByTitleWhenProviderNumberDiffers() {
        var localTrack = Local(EntityKindRegistry.AudioTrack.Code, "Closer", 12);
        var providerTrack = Proposal(ProposalKind.AudioTrack, "Closer", ("trackNumber", 11));

        var match = StructuralChildMatcher.FindProviderChild(
            localTrack,
            [providerTrack],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerTrack, match);
    }

    [Fact]
    public void FilenameStyleEpisodeTitleCanOverrideAMisleadingEpisodeNumber() {
        var localEpisode = Local(EntityKindRegistry.Video.Code, "Show.Name.S03E49.The_Sign.1080p", 49);
        var providerEpisode = Proposal(ProposalKind.VideoEpisode, "The Sign", ("episodeNumber", 50));

        var match = StructuralChildMatcher.FindProviderChild(
            localEpisode,
            [providerEpisode],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerEpisode, match);
    }

    [Fact]
    public void FilenameStyleTrackTitleMatchesProviderDiacritics() {
        var localTrack = Local(EntityKindRegistry.AudioTrack.Code, "01-beyonce-deja_vu", 1);
        var providerTrack = Proposal(ProposalKind.AudioTrack, "Déjà Vu", ("trackNumber", 8));

        var match = StructuralChildMatcher.FindProviderChild(
            localTrack,
            [providerTrack],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerTrack, match);
    }

    [Fact]
    public void TwoDiscFilenameTitlesBindEveryProviderTrackWhenSortOrdersRepeatPerDisc() {
        var localTitlesByDisc = new[] {
            new[] {
                "101-billy_joel-piano_man",
                "102-billy_joel-youre_my_home",
                "103-billy_joel-captain_jack",
                "104-billy_joel-the_entertainer",
                "105-billy_joel-say_goodbye_to_hollywood",
                "106-billy_joel-miami_2017_(seen_the_lights_go_out_on_broadway)",
                "107-billy_joel-new_york_state_of_mind",
                "108-billy_joel-shes_always_a_woman",
                "109-billy_joel-movin_out_(anthonys_song)",
                "110-billy_joel-only_the_good_die_young",
                "111-billy_joel-just_the_way_you_are",
                "112-billy_joel-honesty",
                "113-billy_joel-my_life",
                "114-billy_joel-its_still_rock_and_roll_to_me",
                "115-billy_joel-you_may_be_right",
                "116-billy_joel-dont_ask_me_why",
                "117-billy_joel-shes_got_a_way_(live)",
                "118-billy_joel-allentown"
            },
            new[] {
                "201-billy_joel-goodnight_saigon",
                "202-billy_joel-an_innocent_man",
                "203-billy_joel-uptown_girl",
                "204-billy_joel-the_longest_time",
                "205-billy_joel-tell_her_about_it",
                "206-billy_joel-leave_a_tender_moment_alone",
                "207-billy_joel-a_matter_of_trust",
                "208-billy_joel-baby_grand_(duet_with_ray_charles)",
                "209-billy_joel-i_go_to_extremes",
                "210-billy_joel-we_didnt_start_the_fire",
                "211-billy_joel-leningrad",
                "212-billy_joel-the_downeaster_alexa",
                "213-billy_joel-and_so_it_goes",
                "214-billy_joel-the_river_of_dreams",
                "215-billy_joel-all_about_soul_(remix)",
                "216-billy_joel-lullabye_(goodnight_my_angel)",
                "217-billy_joel-waltz_1_(nunleys_carousel)",
                "218-billy_joel-invention_in_c_minor"
            }
        };
        var providerTitlesByDisc = new[] {
            new[] {
                "Piano Man",
                "You’re My Home",
                "Captain Jack",
                "The Entertainer",
                "Say Goodbye to Hollywood",
                "Miami 2017 (Seen the Lights Go Out on Broadway)",
                "New York State of Mind",
                "She’s Always a Woman",
                "Movin’ Out (Anthony’s Song)",
                "Only the Good Die Young",
                "Just the Way You Are",
                "Honesty",
                "My Life",
                "It’s Still Rock and Roll to Me",
                "You May Be Right",
                "Don’t Ask Me Why",
                "She’s Got a Way",
                "Allentown"
            },
            new[] {
                "Goodnight Saigon",
                "An Innocent Man",
                "Uptown Girl",
                "The Longest Time",
                "Tell Her About It",
                "Leave a Tender Moment Alone",
                "A Matter of Trust",
                "Baby Grand",
                "I Go to Extremes",
                "We Didn’t Start the Fire",
                "Leningrad",
                "The Downeaster “Alexa”",
                "And So It Goes",
                "The River of Dreams",
                "All About Soul (remix)",
                "Lullabye (Goodnight, My Angel)",
                "Waltz #1 (Nunley’s Carousel)",
                "Invention in C Minor"
            }
        };
        var localsByDisc = localTitlesByDisc
            .Select(disc => disc.Select((title, track) => Local(EntityKindRegistry.AudioTrack.Code, title, track)).ToArray())
            .ToArray();
        var providers = providerTitlesByDisc
            .SelectMany(disc => disc)
            .Select((title, globalIndex) => Proposal(ProposalKind.AudioTrack, title, ("sortOrder", globalIndex)))
            .ToArray();
        var localsInPersistedOrder = Enumerable.Range(0, 18)
            .SelectMany(track => localsByDisc.Select(disc => disc[track]))
            .ToArray();
        var expectedProviderByLocalId = localsByDisc
            .SelectMany((disc, discIndex) => disc.Select((local, track) => new {
                local.EntityId,
                Provider = providers[(discIndex * 18) + track]
            }))
            .ToDictionary(pair => pair.EntityId, pair => pair.Provider);
        var usedProviderIndexes = new HashSet<int>();

        var matches = localsInPersistedOrder.Select(local => new {
            Local = local,
            Provider = StructuralChildMatcher.FindProviderChild(
                local,
                providers,
                usedProviderIndexes,
                cautious: false)
        }).ToArray();

        Assert.Equal(36, matches.Length);
        Assert.All(matches, match => Assert.Same(expectedProviderByLocalId[match.Local.EntityId], match.Provider));

        var usedLocalEntityIds = new HashSet<Guid>();
        var localsMatchedFromProvider = providers
            .Select(provider => StructuralChildMatcher.FindLocalChild(
                provider,
                localsInPersistedOrder,
                usedLocalEntityIds,
                cautious: false))
            .ToArray();
        var localsInProviderOrder = localsByDisc.SelectMany(disc => disc).ToArray();
        Assert.All(
            localsMatchedFromProvider.Select((local, index) => new { Local = local, Expected = localsInProviderOrder[index] }),
            match => Assert.Same(match.Expected, match.Local));
    }

    private static StructuralLocalChild Local(string kindCode, string title, int? sortOrder) =>
        new(Guid.NewGuid(), kindCode, title, sortOrder);

    private static EntityMetadataProposal Proposal(
        ProposalKind kind,
        string title,
        (string Code, int Value) position,
        IReadOnlyDictionary<string, string>? externalIds = null) =>
        new(
            ProposalId: $"{kind.ToCode()}:{position.Value}:{title}",
            Provider: "test-provider",
            TargetKind: kind,
            Confidence: 1m,
            MatchReason: "fixture",
            Patch: new EntityMetadataPatch(
                title,
                Description: null,
                ExternalIds: externalIds ?? new Dictionary<string, string>(),
                Urls: [],
                Tags: [],
                Studio: null,
                Credits: [],
                Dates: new Dictionary<string, string>(),
                Stats: new Dictionary<string, int>(),
                Positions: new Dictionary<string, int> { [position.Code] = position.Value },
                Classification: null),
            Images: [],
            Children: [],
            Candidates: [],
            TargetEntityId: null,
            Relationships: []);
}
