using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Applies missing credits from exact manager or configured metadata identities.</summary>
internal sealed class ExternalPeopleEnrichmentRunner(
    PrismediaDbContext db,
    IExternalPeopleEnrichmentPlanResolver plans,
    IIntegrationManagerCreationGateway managers,
    IPluginRequestProposalSource proposals,
    IExternalPeopleCreditsApplier creditsApplier,
    TimeProvider timeProvider) : IExternalPeopleEnrichmentRunner {
    private const int MaximumCredits = 1000;

    public async Task<ExternalPeopleEnrichmentResult> RunAsync(
        Guid holdingId,
        Guid entityId,
        string fingerprint,
        CancellationToken cancellationToken) {
        var plan = await plans.ResolveAsync(holdingId, cancellationToken);
        if (plan is null || plan.EntityId != entityId ||
            !string.Equals(plan.Fingerprint, fingerprint, StringComparison.Ordinal)) {
            return new(false, null, "People metadata configuration changed; a fresh lookup will be scheduled");
        }

        if (await HasCreditsAsync(entityId, cancellationToken)) {
            await RecordAttemptAsync(holdingId, fingerprint, completed: true, cancellationToken);
            return new(false, null, "Existing credits preserved");
        }

        Exception? transientFailure = null;
        if (plan.Manager is not null) {
            try {
                var proposal = await ResolveManagerProposalAsync(plan, cancellationToken);
                if (proposal is not null && await ApplyCreditsAsync(plan, proposal, cancellationToken) is { } applyResult) {
                    if (applyResult == ExternalPeopleCreditsApplyResult.Applied) {
                        await RecordAttemptAsync(holdingId, fingerprint, completed: true, cancellationToken);
                        return new(true, proposal.Provider, $"Applied exact people metadata from {proposal.Provider}");
                    }
                    if (applyResult is ExternalPeopleCreditsApplyResult.ExistingCredits
                        or ExternalPeopleCreditsApplyResult.ProtectedByUser) {
                        await RecordAttemptAsync(holdingId, fingerprint, completed: true, cancellationToken);
                        return new(false, null, applyResult == ExternalPeopleCreditsApplyResult.ProtectedByUser
                            ? "User-protected credits preserved"
                            : "Concurrent credits preserved");
                    }
                }
            } catch (Exception exception) when (exception is IntegrationInvocationException
                or ConnectionNotFoundException
                or ConnectionSecretUnavailableException
                or ConnectionCapabilityUnavailableException) {
                transientFailure = exception;
            }
        }

        var descriptor = RequestKindRegistry.All.SingleOrDefault(candidate =>
            candidate.PluginEntityKind == plan.EntityKind &&
            candidate.Kind is RequestMediaKind.Movie or RequestMediaKind.Series);
        if (descriptor is not null) {
            foreach (var route in plan.MetadataRoutes) {
                try {
                    var proposal = await proposals.ResolveProposalAsync(
                        descriptor,
                        route,
                        hideNsfw: false,
                        includeChildren: false,
                        cancellationToken);
                    if (proposal is not null && await ApplyCreditsAsync(plan, proposal, cancellationToken) is { } applyResult) {
                        if (applyResult == ExternalPeopleCreditsApplyResult.Applied) {
                            await RecordAttemptAsync(holdingId, fingerprint, completed: true, cancellationToken);
                            return new(true, proposal.Provider, $"Applied exact people metadata from {proposal.Provider}");
                        }
                        if (applyResult is ExternalPeopleCreditsApplyResult.ExistingCredits
                            or ExternalPeopleCreditsApplyResult.ProtectedByUser) {
                            await RecordAttemptAsync(holdingId, fingerprint, completed: true, cancellationToken);
                            return new(false, null, applyResult == ExternalPeopleCreditsApplyResult.ProtectedByUser
                                ? "User-protected credits preserved"
                                : "Concurrent credits preserved");
                        }
                    }
                } catch (PluginProviderUnavailableException exception) {
                    transientFailure = exception;
                }
            }
        }

        if (transientFailure is not null) {
            throw transientFailure;
        }

        await RecordAttemptAsync(holdingId, fingerprint, completed: false, cancellationToken);
        return new(false, null, "No exact provider credits were available");
    }

    public Task RecordTerminalAttemptAsync(
        Guid holdingId,
        string fingerprint,
        CancellationToken cancellationToken) =>
        RecordAttemptAsync(holdingId, fingerprint, completed: false, cancellationToken);

    private async Task<EntityMetadataProposal?> ResolveManagerProposalAsync(
        ExternalPeopleEnrichmentPlan plan,
        CancellationToken cancellationToken) {
        var manager = plan.Manager!;
        var work = new ManagedLookupInput(plan.EntityKind, plan.Item.ExpectedExternalIds);
        var result = await managers.LookupAsync(
            manager.Manifest.Id,
            manager.Context,
            work,
            cancellationToken);
        ManagedCreationEvidence.ValidateLookup(work, result);
        var credits = result.Candidate.Metadata?.Credits;
        if (credits is not { Count: > 0 }) {
            return null;
        }
        ValidateCredits(credits);
        return ManagedMetadataProposalFactory.Create(
            manager.Connection.State.Id,
            manager.Manifest.Id,
            result.Candidate) with { TargetEntityId = plan.EntityId };
    }

    private async Task<ExternalPeopleCreditsApplyResult?> ApplyCreditsAsync(
        ExternalPeopleEnrichmentPlan plan,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        if (proposal.Patch?.Credits is not { Count: > 0 }) {
            return null;
        }

        var people = (proposal.Relationships ?? [])
            .Where(relationship => relationship.TargetKind == EntityKind.Person)
            .ToArray();
        var creditsOnly = proposal with {
            TargetEntityId = plan.EntityId,
            Images = [],
            Children = [],
            Relationships = people
        };
        var result = await creditsApplier.ApplyIfMissingAsync(
            plan.EntityId,
            creditsOnly,
            cancellationToken);
        if (result == ExternalPeopleCreditsApplyResult.LifecycleConflict) {
            throw new EntityLifecycleMutationConflictException(plan.EntityId);
        }
        return result;
    }

    private async Task RecordAttemptAsync(
        Guid holdingId,
        string fingerprint,
        bool completed,
        CancellationToken cancellationToken) {
        var now = timeProvider.GetUtcNow();
        var holding = await db.ManagedHoldings
            .SingleOrDefaultAsync(row => row.Id == holdingId && row.ReleasedAt == null, cancellationToken);
        if (holding is null) {
            return;
        }
        holding.PeopleEnrichmentFingerprint = fingerprint;
        if (completed) {
            holding.PeopleEnrichmentCompletedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> HasCreditsAsync(Guid entityId, CancellationToken cancellationToken) {
        var relationshipCodes = new[] {
            RelationshipKind.Cast.ToCode(),
            RelationshipKind.Credits.ToCode()
        };
        return db.EntityRelationshipLinks.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId && relationshipCodes.Contains(row.RelationshipCode),
            cancellationToken);
    }

    private static void ValidateCredits(IReadOnlyList<ManagedPersonCredit> credits) {
        if (credits.Count > MaximumCredits || credits.Any(credit =>
                credit is null ||
                string.IsNullOrWhiteSpace(credit.Name) ||
                credit.Name.Length > 512 ||
                credit.Name.Any(char.IsControl) ||
                !Enum.IsDefined(credit.Role) ||
                credit.Character is { } character && (string.IsNullOrWhiteSpace(character)
                    || character.Length > 512 || character.Any(char.IsControl)) ||
                credit.SortOrder is < 0 or > 1_000_000 ||
                credit.ExternalIds is { Count: > 8 } ||
                credit.ExternalIds?.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
                    || pair.Key.Length > 128 || string.IsNullOrWhiteSpace(pair.Value)
                    || pair.Value.Length > 2048) == true ||
                credit.ExternalIds?.ContainsKey(ExternalIdProviders.Tmdb) == true
                    && !CanonicalTmdbPersonId(credit.ExternalIds[ExternalIdProviders.Tmdb]) ||
                credit.ProfileUrl is { } profileUrl && !SafeImageUrl(profileUrl))) {
            throw new IntegrationInvocationException("The manager returned invalid or oversized people metadata.");
        }
        try {
            _ = credits.SelectMany(credit => (credit.ExternalIds ?? new Dictionary<string, string>())
                .Select(pair => new ExternalIdentity(pair.Key, pair.Value))).ToArray();
        } catch (ArgumentException exception) {
            throw new IntegrationInvocationException($"The manager returned invalid people identities: {exception.Message}");
        }
    }

    private static bool CanonicalTmdbPersonId(string value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)
        && id > 0
        && id.ToString(System.Globalization.CultureInfo.InvariantCulture) == value;

    private static bool SafeImageUrl(string value) =>
        value.Length <= 8192
        && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\')
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0
        && !uri.IsLoopback;
}
