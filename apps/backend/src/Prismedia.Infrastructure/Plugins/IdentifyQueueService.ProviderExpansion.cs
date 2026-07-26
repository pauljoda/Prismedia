using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class IdentifyQueueService {
    private async Task EnqueueCascadeIfNeededAsync(
        IdentifyQueueItemRow row,
        EntityRow entity,
        string provider,
        IdentifyQuery? query,
        EntityMetadataProposal proposal,
        bool hideNsfw,
        bool isForeground,
        CancellationToken cancellationToken) {
        var structuralChildren = EntityKindRegistry.EnumeratesIdentifyChildren(entity.KindCode)
            ? await EligibleProviderChildrenAsync(entity.Id, provider, cancellationToken)
            : [];
        var needsRelationshipCascade = await HasHydratableRelationshipsAsync(provider, proposal, cancellationToken);
        if (structuralChildren.Count == 0 && !needsRelationshipCascade) {
            return;
        }

        Guid? predecessorId = row.SearchJobId;
        if (needsRelationshipCascade) {
            var relationshipJob = await AppendIdentifyProviderCallAsync(
                row,
                entity,
                parentEntityId: null,
                provider,
                query,
                parentExternalIds: null,
                hideNsfw,
                hydrateRelationships: true,
                proposal.ProposalId,
                predecessorId,
                isForeground,
                cancellationToken);
            predecessorId = relationshipJob.Id;
        }

        foreach (var child in structuralChildren) {
            var childJob = await AppendIdentifyProviderCallAsync(
                row,
                child,
                entity.Id,
                provider,
                query: null,
                proposal.Patch.ExternalIds,
                hideNsfw,
                hydrateRelationships: true,
                proposal.ProposalId,
                predecessorId,
                isForeground,
                cancellationToken);
            row.CascadeJobId ??= childJob.Id;
        }
    }

    private async Task<JobRunSnapshot> AppendIdentifyProviderCallAsync(
        IdentifyQueueItemRow row,
        EntityRow target,
        Guid? parentEntityId,
        string provider,
        IdentifyQuery? query,
        IReadOnlyDictionary<string, string>? parentExternalIds,
        bool hideNsfw,
        bool hydrateRelationships,
        string expectedProposalId,
        Guid? predecessorId,
        bool isForeground,
        CancellationToken cancellationToken) {
        var payload = new IdentifyProviderCallPayload(
            row.EntityId,
            target.Id,
            parentEntityId,
            provider,
            query,
            parentExternalIds,
            hideNsfw,
            hydrateRelationships,
            expectedProposalId);
        var request = new EnqueueJobRequest(
            JobType.IdentifyProviderCall,
            payload.ToJson(),
            TargetEntityKind: target.KindCode,
            TargetEntityId: target.Id.ToString(),
            TargetLabel: target.Title,
            Origin: isForeground ? JobGraphOrigin.Interactive : JobGraphOrigin.Background);
        var resourceKey = await DeclarePluginResourceAsync(provider, target.KindCode, cancellationToken);
        if (_graphs is not null && row.JobGraphId is { } graphId) {
            return await _graphs.AppendNodeAsync(
                graphId,
                new GraphJobNodeRequest(
                    $"identify-provider:{provider}:{target.Id}",
                    request,
                    ParentRunId: predecessorId,
                    DependsOn: predecessorId is { } predecessor ? [predecessor] : null,
                    Importance: JobNodeImportance.Required,
                    ResourceClass: JobResourceClass.Light,
                    ResourceKey: resourceKey),
                cancellationToken);
        }

        return await _jobs.EnqueueAsync(request with { ResourceKey = resourceKey }, cancellationToken);
    }

    private async Task<string?> DeclarePluginResourceAsync(
        string provider,
        string entityKind,
        CancellationToken cancellationToken) {
        if (await _identify.GetExecutionPolicyAsync(provider, entityKind, cancellationToken) is not { } execution) {
            return null;
        }

        var resourceKey = JobResourceKeys.Plugin(provider);
        await _jobs.DeclareResourceAsync(
            resourceKey,
            execution.MaxConcurrentInvocations,
            TimeSpan.FromMilliseconds(execution.MinimumStartIntervalMs),
            cancellationToken);
        return resourceKey;
    }

    private async Task<IReadOnlyList<EntityRow>> EligibleProviderChildrenAsync(
        Guid parentEntityId,
        string provider,
        CancellationToken cancellationToken) {
        var children = await _db.Entities.AsNoTracking()
            .Where(child => child.ParentEntityId == parentEntityId)
            .OrderBy(child => child.SortOrder)
            .ThenBy(child => child.Id)
            .ToArrayAsync(cancellationToken);
        if (children.Length == 0) return [];

        var eligibility = await _eligibility.EvaluateManyAsync(
            children.Select(child => child.Id).ToArray(),
            cancellationToken);
        var supportByKind = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EntityRow>();
        foreach (var child in children.Where(child => eligibility[child.Id].IsEligible)) {
            if (!supportByKind.TryGetValue(child.KindCode, out var supported)) {
                supported = (await _identify.ListProvidersAsync(child.KindCode, cancellationToken))
                    .Any(candidate => candidate.Enabled && candidate.Id.Equals(provider, StringComparison.OrdinalIgnoreCase));
                supportByKind[child.KindCode] = supported;
            }
            if (supported) result.Add(child);
        }
        return result;
    }

    private async Task<bool> HasHydratableRelationshipsAsync(
        string provider,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        var relationships = EntityMetadataProposalTraversal.Relationships(proposal)
            .Concat((proposal.Children ?? []).Where(child => EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind)))
            .ToArray();
        if (relationships.Length == 0) {
            return false;
        }

        var supportByKind = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in relationships) {
            var patch = relationship.Patch;
            var hasLookupInput =
                (patch.ExternalIds?.Count ?? 0) > 0 ||
                (patch.Urls?.Count ?? 0) > 0 ||
                !string.IsNullOrWhiteSpace(patch.Title);
            if (!hasLookupInput) {
                continue;
            }

            var kindCode = relationship.TargetKind.ToEntityKind().ToCode();
            if (!supportByKind.TryGetValue(kindCode, out var supportsKind)) {
                var providers = await _identify.ListProvidersAsync(kindCode, cancellationToken);
                supportsKind = providers.Any(candidate =>
                    candidate.Id.Equals(provider, StringComparison.OrdinalIgnoreCase) && candidate.Enabled);
                supportByKind[kindCode] = supportsKind;
            }

            if (supportsKind) {
                return true;
            }
        }

        return false;
    }

    /// <summary>Declares the strict durable provider resource required before a search can be claimed.</summary>
    private async Task<string?> DeclareSearchResourceAsync(
        string? providerCode,
        EntityRow entity,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (providerCode is not null &&
            await _identify.GetExecutionPolicyAsync(providerCode, entity.KindCode, cancellationToken) is { } execution) {
            var pluginKey = JobResourceKeys.Plugin(providerCode);
            await _jobs.DeclareResourceAsync(
                pluginKey,
                execution.MaxConcurrentInvocations,
                TimeSpan.FromMilliseconds(execution.MinimumStartIntervalMs),
                cancellationToken);
            return pluginKey;
        }

        if (providerCode is not null) {
            return null;
        }

        // A provider walk may reach a rate-limited plugin after trying an unrestricted one. Give
        // the whole walk one durable cross-worker resource whenever any participating provider
        // declares a policy, using the strictest declared values so that provider can never be
        // overlapped or started too quickly by another unscoped lane.
        var providers = await ResolveSearchProvidersAsync(null, entity, hideNsfw, cancellationToken);
        var policies = new List<PluginExecutionPolicy>();
        foreach (var provider in providers) {
            if (await _identify.GetExecutionPolicyAsync(provider, entity.KindCode, cancellationToken) is { } policy) {
                policies.Add(policy);
            }
        }

        if (policies.Count == 0) {
            return null;
        }

        await _jobs.DeclareResourceAsync(
            JobResourceKeys.IdentifyProviderWalk,
            policies.Min(policy => policy.MaxConcurrentInvocations),
            TimeSpan.FromMilliseconds(policies.Max(policy => policy.MinimumStartIntervalMs)),
            cancellationToken);
        return JobResourceKeys.IdentifyProviderWalk;
    }
}
