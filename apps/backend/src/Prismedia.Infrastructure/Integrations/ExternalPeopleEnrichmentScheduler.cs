using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Queues one durable exact people lookup per saved identity/provider configuration.</summary>
internal sealed class ExternalPeopleEnrichmentScheduler(
    PrismediaDbContext db,
    IExternalPeopleEnrichmentPlanResolver plans,
    IEntityExternalIdentityStore externalIdentities,
    IEntityLifecycleMutationLease lifecycle,
    IJobQueueService jobs,
    TimeProvider timeProvider) : IExternalPeopleEnrichmentScheduler {
    #region Actions - Scheduling

    public async Task ScheduleAsync(Guid holdingId, CancellationToken cancellationToken) {
        var plan = await plans.ResolveAsync(holdingId, cancellationToken);
        if (plan is null) {
            return;
        }

        var holding = await db.ManagedHoldings.SingleAsync(row => row.Id == holdingId, cancellationToken);
        if (holding.PeopleEnrichmentCompletedAt is not null
            || string.Equals(holding.PeopleEnrichmentFingerprint, plan.Fingerprint, StringComparison.Ordinal)) {
            return;
        }

        if (await HasCreditsAsync(plan.EntityId, cancellationToken)) {
            holding.PeopleEnrichmentFingerprint = plan.Fingerprint;
            holding.PeopleEnrichmentCompletedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await AttachPinnedIdentitiesAsync(plan, cancellationToken);
        if (await jobs.HasPendingAsync(JobType.AutoIdentify, plan.EntityId.ToString(), cancellationToken)) {
            return;
        }

        var payload = new AutoIdentifyJobPayload(
            IgnoreOrganizedGate: true,
            ExternalPeopleHoldingId: holdingId,
            ExternalPeopleFingerprint: plan.Fingerprint);
        await jobs.EnqueueAsync(EnqueueJobRequest.ForEntity(
            JobType.AutoIdentify,
            plan.EntityKind,
            plan.EntityId.ToString(),
            holding.Title,
            payloadJson: payload.ToJson()), cancellationToken);
    }

    private async Task AttachPinnedIdentitiesAsync(
        ExternalPeopleEnrichmentPlan plan,
        CancellationToken cancellationToken) {
        var incoming = plan.Item.ExpectedExternalIds
            .Select(pair => new EntityExternalId(new ExternalIdentity(pair.Key, pair.Value), null))
            .ToArray();
        if (!await lifecycle.ExecuteAsync(plan.EntityId, async leaseToken => {
            var resolution = await externalIdentities.ResolveAsync(
                plan.EntityKind,
                incoming.Select(value => value.Identity).ToArray(),
                parentEntityId: null,
                leaseToken);
            if (resolution.Matches.Any(match => match.EntityId != plan.EntityId)) {
                throw new ArgumentException(
                    "A pinned manager identity belongs to another local entity. Review duplicate metadata before enrichment.");
            }

            var existing = await externalIdentities.ListAsync(plan.EntityId, leaseToken);
            if (existing.Any(saved => plan.Item.ExpectedExternalIds.TryGetValue(saved.Provider, out var expected)
                && !string.Equals(saved.Value, expected, StringComparison.Ordinal))) {
                throw new ArgumentException(
                    "The local metadata identity conflicts with the connected holding. Review it before enrichment.");
            }

            await externalIdentities.WriteAsync(
                plan.EntityId,
                incoming,
                ExternalIdentityWriteMode.AddMissing,
                leaseToken);
            await db.SaveChangesAsync(leaseToken);
        }, cancellationToken)) {
            throw new EntityLifecycleMutationConflictException(plan.EntityId);
        }
    }

    #endregion

    #region Actions - Queries

    private Task<bool> HasCreditsAsync(Guid entityId, CancellationToken cancellationToken) {
        var relationshipCodes = new[] {
            RelationshipKind.Cast.ToCode(),
            RelationshipKind.Credits.ToCode()
        };
        return db.EntityRelationshipLinks.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId && relationshipCodes.Contains(row.RelationshipCode),
            cancellationToken);
    }

    #endregion
}
