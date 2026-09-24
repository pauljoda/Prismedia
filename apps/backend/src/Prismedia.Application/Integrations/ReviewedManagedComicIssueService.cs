using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Reviews and atomically accepts one missing issue in an already existing comic manager run.</summary>
public sealed class ReviewedManagedComicIssueService(
    ManagedLibraryService library,
    ExternalLibraryService externalLibraries,
    IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway creation,
    IManagedComicIssueWriter writer,
    IManagedRequestStore store,
    ManagedRequestService requests,
    IReviewedManagedRequestCommitScope commitScope) {
    /// <summary>Re-reads the run, exact issue, and mapped destination without creating local or remote work.</summary>
    public async Task<ReviewedManagedComicIssue> ReviewAsync(Guid connectionId,
        ReviewManagedComicIssueInput input, CancellationToken token) {
        if (input.Item.EntityKind != EntityKind.ComicSeries
            || string.IsNullOrWhiteSpace(input.RemoteIssueId) || input.RemoteIssueId.Length > 512
            || string.IsNullOrWhiteSpace(input.IssueLabel) || input.IssueLabel.Length > 128)
            throw new ArgumentException("Select one exact comic issue from its connected run.");
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ExternalManager,
            IntegrationOperation.LookupManaged, EntityKind.ComicSeries, token);
        foreach (var operation in new[] { IntegrationOperation.ReconcileManaged,
                     IntegrationOperation.ConfigureManaged, IntegrationOperation.RequestManaged })
            await access.RequireAsync(connectionId, PluginCapability.ExternalManager, operation, EntityKind.ComicSeries, token);
        var snapshot = await library.GetAsync(connectionId, input.Item, token);
        var issue = snapshot.ComicIssues?.SingleOrDefault(issue => issue.RemoteId == input.RemoteIssueId
            && issue.IssueLabel == input.IssueLabel)
            ?? throw new ArgumentException("The selected comic issue changed. Refresh the connected run.");
        if (snapshot.Files.Any(file => file.Targets.Any(target => target.RemoteId == issue.RemoteId)))
            throw new ArgumentException("This issue already has a final file. Link its local source instead of requesting it again.");
        if (!snapshot.Item.ExternalIds.TryGetValue(ExternalIdProviders.ComicVine, out var seriesId)
            || !seriesId.StartsWith(ComicVineIdentityFormats.SeriesPrefix, StringComparison.Ordinal)
            || issue.ExternalIds is not { Count: 1 }
            || !issue.ExternalIds.TryGetValue(ExternalIdProviders.ComicVine, out var issueId)
            || !issueId.StartsWith(ComicVineIdentityFormats.IssuePrefix, StringComparison.Ordinal))
            throw new ArgumentException("The connected run and issue need exact Comic Vine identities.");
        var work = new ManagedLookupInput(EntityKind.ComicSeries,
            new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = seriesId },
            [new(EntityKind.ComicInstallment,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = issueId },
                IssueLabel: issue.IssueLabel)]);
        var lookup = await creation.LookupAsync(authorized.Manifest.Id, authorized.Context, work, token);
        ManagedCreationEvidence.ValidateLookup(work, lookup);
        if (lookup.Existing?.Item.RemoteId != snapshot.Item.RemoteId
            || lookup.Targets is not { Count: 1 } || lookup.Targets[0].RemoteId != issue.RemoteId)
            throw new IntegrationInvocationException("The manager lookup no longer resolves this exact run and issue.");
        var mounts = (await externalLibraries.ListSuitableAsync(connectionId, EntityKind.ComicSeries, token))
            .Where(mount => RemoteLibraryPath.Parse(mount.RemotePath).IsAncestorOf(snapshot.Path))
            .OrderByDescending(mount => mount.RemotePath.Length).ToArray();
        if (mounts.Length == 0 || mounts.Length > 1 && mounts[0].RemotePath.Length == mounts[1].RemotePath.Length)
            throw new ArgumentException("Map this comic run to one enabled local library before requesting an issue.");
        var mount = mounts[0];
        var options = await library.OptionsAsync(connectionId, new(EntityKind.ComicSeries), token);
        if (!options.Roots.Any(root => root.Id == mount.RemoteRootId && root.Path == mount.RemotePath && root.Accessible != false))
            throw new ArgumentException("The mapped comic root changed. Review its connection before requesting an issue.");
        var active = (await store.ListAsync(connectionId, token)).Where(request =>
            request.Operation.State.Phase is not (ManagedRequestPhase.Cancelled or ManagedRequestPhase.OwnershipReleased)
            && ManagedRequestIdentity.SameWork(request.Plan.Request.ReviewedWork, work)).ToArray();
        if (active.Length > 1)
            throw new ManagedRequestConflictException("More than one active request matches this exact issue. Review request activity.");
        return new(authorized.Connection.State.Revision, input.Item, snapshot.Item.Title, issue, mount,
            options, work, lookup.Existing, snapshot.Item.Monitored,
            active.Length == 1 ? ManagedRequestService.Map(active[0]) : null);
    }

    /// <summary>Commits the wanted identity and one durable external owner in the same local transaction.</summary>
    public async Task<CommitManagedComicIssueResponse> CommitAsync(Guid connectionId,
        CommitManagedComicIssueInput input, CancellationToken token) {
        if (input.OperationId == Guid.Empty || input.LibraryRootId == Guid.Empty
            || string.IsNullOrWhiteSpace(input.ComicVineIssueId))
            throw new ArgumentException("Choose a reviewed comic issue and stable operation ID.");
        var fingerprint = Fingerprint(connectionId, input);
        if (await requests.FindReviewedAsync(connectionId, input.OperationId, fingerprint, token) is { } replay)
            return new(replay.EntityId, replay.TargetEntityIds!.Single(), replay);
        var review = await ReviewAsync(connectionId,
            new(input.Item, input.RemoteIssueId, input.IssueLabel), token);
        if (review.ConnectionRevision != input.ExpectedConnectionRevision
            || review.Mount.LibraryRootId != input.LibraryRootId
            || review.Issue.ExternalIds?.GetValueOrDefault(ExternalIdProviders.ComicVine) != input.ComicVineIssueId)
            throw new ManagedRequestConflictException("The reviewed comic issue or mapped library changed. Review it again.");
        if (review.ExistingRequest is not null)
            throw new ManagedRequestConflictException("This exact issue already has a request. Open its retained Activity entry.");
        return await commitScope.ExecuteAsync<CommitManagedComicIssueResponse>(connectionId, input.ExpectedConnectionRevision, async ct => {
            var prepared = await writer.EnsureAsync(
                new(ExternalIdProviders.ComicVine, review.Work.ExternalIds[ExternalIdProviders.ComicVine]),
                review.SeriesTitle,
                new(ExternalIdProviders.ComicVine, input.ComicVineIssueId),
                review.Issue.Title, review.Issue.IssueLabel, ct);
            if (prepared.HasFile)
                throw new ManagedRequestConflictException("This issue already has a local source. Open the existing issue instead.");
            var target = await store.RequireTargetAsync(connectionId, prepared.SeriesEntityId,
                input.LibraryRootId, [prepared.IssueEntityId], bookRendition: null, ct);
            if (!ManagedRequestIdentity.SameWork(target.Work, review.Work))
                throw new ManagedRequestConflictException("The wanted issue identity changed during review.");
            var create = new CreateManagedRequestInput(input.OperationId, prepared.SeriesEntityId,
                input.LibraryRootId, target.Work, null, input.Monitored, Search: true,
                TargetEntityIds: [prepared.IssueEntityId]);
            var preview = new ManagedRequestPreview(prepared.SeriesEntityId, review.SeriesTitle,
                review.Work, review.Mount, review.Options, review.Existing, [prepared.IssueEntityId]);
            var accepted = await requests.AcceptAsync(connectionId, create, preview, fingerprint,
                input.ExpectedConnectionRevision, existingHoldingId: null, ct);
            return new(prepared.SeriesEntityId, prepared.IssueEntityId, accepted);
        }, token);
    }

    private static string Fingerprint(Guid connectionId, CommitManagedComicIssueInput input) {
        var canonical = new {
            connectionId,
            input.OperationId,
            input.ExpectedConnectionRevision,
            input.LibraryRootId,
            input.Item.EntityKind,
            input.Item.RemoteId,
            ExternalIds = input.Item.ExpectedExternalIds.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            input.RemoteIssueId,
            input.IssueLabel,
            input.ComicVineIssueId,
            input.Monitored
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }
}
