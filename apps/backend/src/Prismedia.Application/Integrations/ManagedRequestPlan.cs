using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Immutable creation and fulfillment intent; credentials remain on the connection.</summary>
public sealed record ManagedRequestPlan(CreateManagedRequestInput Request, EnsureManagedInput Creation, string Title,
    string Fingerprint, string? ReviewedCommitFingerprint = null, long? ExpectedConnectionRevision = null,
    Guid? ExistingHoldingId = null) {
    #region Actions - Display

    /// <summary>Names finite comic work by its exact issue while retaining the run title.</summary>
    public string DisplayTitle() => Request.ReviewedWork is { EntityKind: EntityKind.ComicSeries, Targets: [{ IssueLabel: { } label }] }
        ? $"{Title} · #{label}"
        : Title;

    #endregion
}
