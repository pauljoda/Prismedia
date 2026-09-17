using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Validates bounded, exact source-preparation observations against the immutable accepted selection.</summary>
internal static class SourceAcquisitionValidation {
    internal const long MaximumPublicationBytes = 2L * 1024 * 1024 * 1024;

    internal static void Validate(SourceAcquisitionObservation observation, SourceSelection selection, string offerId,
        CatalogPublication? publication = null, CatalogOffer? offer = null) {
        if (observation is null || observation.Selection is null || observation.Publication is null || observation.Offer is null
            || observation.Selection != selection || observation.OfferId != offerId
            || observation.Offer?.Id != offerId || !Enum.IsDefined(observation.State)
            || observation.Progress is { } progress && (!double.IsFinite(progress) || progress is < 0 or > 1)
            || observation.Problem?.Length > 4096
            || observation.State == SourceAcquisitionState.Failed && string.IsNullOrWhiteSpace(observation.Problem))
            throw InvalidObservation();
        CatalogDiscoveryService.ValidatePage(new("Source preparation", [new(selection, false, observation.Publication, [observation.Offer])]),
            new(selection.EntityKind, Limit: 1));
        var expectedAccess = observation.State == SourceAcquisitionState.Ready
            ? AcquisitionAccessKind.Download : AcquisitionAccessKind.Request;
        if (observation.Offer.Access != expectedAccess || string.IsNullOrWhiteSpace(observation.Offer.MediaType)
            || !IntegrationMediaFormats.IsSupportedMediaType(selection.EntityKind, observation.Offer.MediaType)
            || observation.Offer.ByteSize > MaximumPublicationBytes
            || selection.EntityKind == EntityKind.Image && observation.Offer.ByteSize > IntegrationMediaFormats.MaximumImageBytes
            || publication is not null && !SamePublication(publication, observation.Publication)
            || offer is not null && (offer.Id != observation.Offer.Id || offer.MediaType != observation.Offer.MediaType
                || offer.ByteSize != observation.Offer.ByteSize))
            throw InvalidObservation();
    }

    private static bool SamePublication(CatalogPublication expected, CatalogPublication actual) =>
        expected.Title == actual.Title && expected.Description == actual.Description
        && expected.Authors.SequenceEqual(actual.Authors)
        && expected.ExternalIds.OrderBy(pair => pair.Key).SequenceEqual(actual.ExternalIds.OrderBy(pair => pair.Key))
        && expected.Language == actual.Language && expected.Publisher == actual.Publisher
        && expected.EditionLabel == actual.EditionLabel && expected.IssueLabel == actual.IssueLabel
        && expected.Attribution == actual.Attribution;

    private static IntegrationInvocationException InvalidObservation() =>
        new("The source returned invalid, changed, or unbounded preparation evidence.");
}
