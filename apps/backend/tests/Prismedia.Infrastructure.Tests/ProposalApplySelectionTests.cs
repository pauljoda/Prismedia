using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class ProposalApplySelectionTests {
    [Fact]
    public void SelectAllPresentFieldsIncludesTypedDatesWithoutLegacyDates() {
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:123",
            Provider: "tmdb",
            TargetKind: ProposalKind.Movie,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: new EntityMetadataPatch(
                Title: null,
                Description: null,
                ExternalIds: new Dictionary<string, string>(),
                Urls: [],
                Tags: [],
                Studio: null,
                Credits: [],
                Dates: new Dictionary<string, string>(),
                Stats: new Dictionary<string, int>(),
                Positions: new Dictionary<string, int>(),
                Classification: null) {
                    DateEntries = [new EntityMetadataDatePatch(EntityDateType.StreamingRelease, "2026-11-01")]
                },
            Images: [],
            Children: [],
            Candidates: []);

        Assert.Contains(
            MetadataPatchField.Dates.ToCode(),
            ProposalApplySelection.SelectAllPresentFields(proposal));
    }
}
