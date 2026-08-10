using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Shared relationship-shell hydration used by both Identify and pre-commit Request review cascades.
/// Stable proposal ids let every client replace a card without losing review state.
/// </summary>
internal static class PluginRelationshipProposalHydrator {
    public static async Task<EntityMetadataProposal> HydrateAsync(
        EntityMetadataProposal relationship,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        bool includeNsfw,
        IdentifyRunnerSelector runners,
        CancellationToken cancellationToken) {
        if (!descriptor.Manifest.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, relationship.TargetKind.ToCode()))) {
            return relationship;
        }

        var patch = relationship.Patch;
        var externalIds = patch.ExternalIds ?? new Dictionary<string, string>();
        var urls = patch.Urls ?? [];
        var title = patch.Title?.Trim() ?? relationship.TargetKind.ToCode();
        var action = externalIds.Count > 0
            ? IdentifyAction.LookupId
            : urls.Count > 0
                ? IdentifyAction.LookupUrl
                : IdentifyAction.Search;
        var query = action switch {
            IdentifyAction.LookupId => new IdentifyQuery(null, null, externalIds),
            IdentifyAction.LookupUrl => new IdentifyQuery(null, urls.FirstOrDefault(), null),
            _ => new IdentifyQuery(title, null, null)
        };
        var request = new IdentifyPluginRequest(
            ProtocolVersion: PluginProtocol.CurrentVersion,
            Action: action,
            Auth: auth,
            Entity: new IdentifyEntitySnapshot(
                Guid.Empty,
                relationship.TargetKind,
                title,
                externalIds,
                urls),
            Query: query,
            Hints: new IdentifyMatchHints(externalIds, urls, title, null),
            StructuralContext: null,
            IncludeNsfw: includeNsfw,
            IncludeRelationshipDetails: false,
            IncludeStructuralChildren: false);

        try {
            var response = await runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
            if (response.Ok && response.Result?.Patch is not null) {
                return response.Result with {
                    TargetEntityId = relationship.TargetEntityId,
                    ProposalId = relationship.ProposalId
                };
            }
        } catch (OperationCanceledException) {
            throw;
        } catch {
            // Relationship detail is best-effort. The shell still carries enough identity to apply.
        }

        return relationship;
    }
}
