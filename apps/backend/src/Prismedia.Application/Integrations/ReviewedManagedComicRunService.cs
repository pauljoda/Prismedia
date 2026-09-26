using System.Globalization;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Reviews and adds one Comic Vine run without monitoring or requesting any issue.</summary>
public sealed class ReviewedManagedComicRunService(
    IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway creation,
    ManagedLibraryService library,
    ExternalLibraryService externalLibraries) {
    #region Actions - Review

    /// <summary>Confirms the exact Comic Vine identity and current mapped destination choices.</summary>
    public async Task<ReviewedManagedComicRun> ReviewAsync(Guid connectionId,
        ReviewManagedComicRunInput input, CancellationToken token) {
        RequireIdentity(input.Identity);
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ExternalManager,
            IntegrationOperation.LookupManaged, EntityKind.ComicSeries, token);
        await access.RequireAsync(connectionId, PluginCapability.ExternalManager,
            IntegrationOperation.EnsureManaged, EntityKind.ComicSeries, token);
        var work = Work(input.Identity);
        var lookup = await creation.LookupAsync(authorized.Manifest.Id, authorized.Context, work, token);
        ManagedCreationEvidence.ValidateLookup(work, lookup);
        var options = await library.OptionsAsync(connectionId, new(EntityKind.ComicSeries), token);
        var mounts = (await externalLibraries.ListSuitableAsync(connectionId, EntityKind.ComicSeries, token))
            .Where(mount => options.Roots.Any(root => root.Id == mount.RemoteRootId
                && root.Path == mount.RemotePath && root.Accessible != false))
            .Where(mount => lookup.Existing is null || RemoteLibraryPath.Parse(mount.RemotePath).IsAncestorOf(lookup.Existing.Path))
            .OrderBy(mount => mount.Label, StringComparer.OrdinalIgnoreCase).ToArray();
        if (mounts.Length == 0) {
            throw new ArgumentException("Map a Kapowarr comic root to an enabled local library before adding this run.");
        }

        var existing = lookup.Existing is { } holding
            ? new ManagedItemInput(EntityKind.ComicSeries, holding.Item.RemoteId, holding.Item.ExternalIds)
            : null;
        return new(authorized.Connection.State.Revision, lookup.Candidate, mounts, existing);
    }

    #endregion

    #region Actions - Commit

    /// <summary>Rechecks the review and asks Kapowarr to create one unmonitored run at most once.</summary>
    public async Task<CommitManagedComicRunResponse> CommitAsync(Guid connectionId,
        CommitManagedComicRunInput input, CancellationToken token) {
        if (input.OperationId == Guid.Empty || input.MountId == Guid.Empty
            || string.IsNullOrWhiteSpace(input.ExpectedTitle)) {
            throw new ArgumentException("Review a Comic Vine run and mapped destination before adding it.");
        }

        var review = await ReviewAsync(connectionId, new(input.Identity), token);
        if (review.ConnectionRevision != input.ExpectedConnectionRevision
            || review.Candidate.Title != input.ExpectedTitle) {
            throw new ManagedRequestConflictException("The connected run changed. Review it again before adding it.");
        }

        var mount = review.Mounts.SingleOrDefault(choice => choice.Id == input.MountId)
            ?? throw new ManagedRequestConflictException("The mapped comic library changed. Review it again.");
        if (review.Existing is { } existing) {
            return new(existing, false);
        }

        var authorized = await access.RequireAsync(connectionId, PluginCapability.ExternalManager,
            IntegrationOperation.EnsureManaged, EntityKind.ComicSeries, token);
        var work = Work(input.Identity);
        var result = await creation.EnsureAsync(authorized.Manifest.Id, authorized.Context,
            new(input.OperationId, work, null, mount.RemoteRootId, mount.RemotePath), token);
        ManagedCreationEvidence.ValidateResult(work, result);
        if (result.Outcome == ManagedMutationOutcome.Rejected) {
            throw new ManagedRequestConflictException(result.Problem ?? "Kapowarr rejected the reviewed run.");
        }

        var holding = result.Holding!;
        if (holding.Item.Monitored || !RemoteLibraryPath.Parse(mount.RemotePath).IsAncestorOf(holding.Path)) {
            throw new IntegrationInvocationException("Kapowarr did not confirm the run in the reviewed root with monitoring off.");
        }

        return new(new(EntityKind.ComicSeries, holding.Item.RemoteId, holding.Item.ExternalIds), result.Created);
    }

    #endregion

    #region Actions - Identity

    private static ManagedLookupInput Work(ExternalIdentity identity) => new(EntityKind.ComicSeries,
        new Dictionary<string, string> { [identity.Namespace] = identity.Value });

    private static void RequireIdentity(ExternalIdentity identity) {
        var format = ManagedFulfillmentPolicy.For(EntityKind.ComicSeries).IdentityFormats[0];
        if (identity?.Namespace != format.Provider || !format.IsCanonical(identity.Value)) {
            throw new ArgumentException("Choose one exact comic run from the manager's search.");
        }
    }

    #endregion
}
