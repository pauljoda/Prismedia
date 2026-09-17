using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Host-owned validation of exact managed-work identity and initial creation evidence.</summary>
public static class ManagedCreationEvidence {
    /// <summary>Rejects mismatched metadata and malformed existing holdings before they influence request state.</summary>
    public static void ValidateLookup(ManagedLookupInput work, ManagedLookupResult result) {
        var candidate = result?.Candidate;
        if (candidate is null || candidate.EntityKind != work.EntityKind || string.IsNullOrWhiteSpace(candidate.Title)
            || candidate.Title.Length > 512 || candidate.Title.Any(char.IsControl) || candidate.Year is < 0 or > 9999
            || candidate.ExternalIds is not { Count: > 0 and <= 64 }
            || candidate.ExternalIds.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128
                || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 2048)
            || work.ExternalIds.Any(pair => candidate.ExternalIds.GetValueOrDefault(pair.Key) != pair.Value))
            throw new IntegrationInvocationException("The manager returned a different or incomplete metadata identity.");
        if (result!.Existing is { } existing) ValidateHolding(work, existing);
    }
    /// <summary>Requires an exact pinned work and the connected-library evidence contract.</summary>
    public static void ValidateHolding(ManagedLookupInput work, ManagedItemSnapshot holding) {
        if (holding?.Item is null) throw new IntegrationInvocationException("The manager did not confirm a holding.");
        ManagedLibraryService.ValidateSnapshot(new(work.EntityKind, holding.Item.RemoteId, work.ExternalIds), holding);
    }
    /// <summary>Only an applied holding or a definite rejection is a valid creation result; all other replies are uncertain.</summary>
    public static void ValidateResult(ManagedLookupInput work, EnsureManagedResult result) {
        if (result is { Outcome: ManagedMutationOutcome.Rejected, Holding: null, Created: false }) return;
        if (result is not { Outcome: ManagedMutationOutcome.Applied, Holding: not null })
            throw new IntegrationInvocationException("The manager did not return a definite creation result. Reconcile its exact identity.");
        ValidateHolding(work, result.Holding);
    }
}
