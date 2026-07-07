using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

public sealed class RequestProposalReadingTests {
    [Fact]
    public void ChildNumberOfUsesSeasonNumberForSeasonChildren() {
        var patch = Patch(new Dictionary<string, int> { ["seasonNumber"] = 6 });

        Assert.Equal(6, RequestProposalReading.ChildNumberOf(RequestMediaKind.Season, patch));
    }

    [Fact]
    public void ChildNumberOfTreatsSeasonZeroAsUnnumberedSpecials() {
        var patch = Patch(new Dictionary<string, int> { ["seasonNumber"] = 0 });

        Assert.Null(RequestProposalReading.ChildNumberOf(RequestMediaKind.Season, patch));
    }

    [Fact]
    public void ChildNumberOfStillUsesVolumeNumberForBookChildren() {
        var patch = Patch(new Dictionary<string, int> { ["volumeNumber"] = 3 });

        Assert.Equal(3, RequestProposalReading.ChildNumberOf(RequestMediaKind.Book, patch));
    }

    private static EntityMetadataPatch Patch(IReadOnlyDictionary<string, int> positions) =>
        new(
            Title: "Child",
            Description: null,
            ExternalIds: new Dictionary<string, string> { ["provider"] = "child" },
            Urls: [],
            Tags: [],
            Studio: null,
            Credits: [],
            Dates: new Dictionary<string, string>(),
            Stats: new Dictionary<string, int>(),
            Positions: positions,
            Classification: null);
}
