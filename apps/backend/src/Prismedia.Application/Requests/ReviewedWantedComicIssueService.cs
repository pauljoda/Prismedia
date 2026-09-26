using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Prepares one missing issue of a comic run the connected manager already holds. The run's own pinning
/// identity and the issue's target identity come from the comic kind's managed-fulfillment policy.
/// </summary>
public sealed class ReviewedWantedComicIssueService(IManagedComicIssueWriter writer) : IManagedConnectedTargetPreparer {
    #region Static Variables

    private const int MaximumRemoteIdLength = 512;
    private const int MaximumLabelLength = 128;

    #endregion

    #region Variables

    /// <inheritdoc />
    public EntityKind Kind => EntityKind.ComicSeries;

    #endregion

    #region Actions - Review

    /// <inheritdoc />
    public ReviewedConnectedTarget Review(ManagedItemSnapshot snapshot, ManagedConnectedTargetInput input) {
        if (string.IsNullOrWhiteSpace(input.RemoteTargetId) || input.RemoteTargetId.Length > MaximumRemoteIdLength
            || string.IsNullOrWhiteSpace(input.TargetLabel) || input.TargetLabel.Length > MaximumLabelLength) {
            throw new ArgumentException("Select one exact comic issue from its connected run.");
        }

        var issue = snapshot.ComicIssues?.SingleOrDefault(candidate => candidate.RemoteId == input.RemoteTargetId
            && candidate.IssueLabel == input.TargetLabel)
            ?? throw new ArgumentException("The selected comic issue changed. Refresh the connected run.");
        if (snapshot.Files.Any(file => file.Targets.Any(target => target.RemoteId == issue.RemoteId))) {
            throw new ArgumentException("This issue already has a final file. Link its local source instead of requesting it again.");
        }

        var policy = ManagedFulfillmentPolicy.For(Kind);
        var seriesIdentity = policy.PinningIdentity(snapshot.Item.ExternalIds);
        var issueIdentity = issue.ExternalIds is { Count: 1 } issueIds ? policy.TargetIdentityFormats[0].Find(issueIds) : null;
        if (seriesIdentity is null || issueIdentity is null) {
            throw new ArgumentException($"The connected run and issue need {policy.IdentityDescription}.");
        }

        var work = new ManagedLookupInput(Kind,
            new Dictionary<string, string> { [seriesIdentity.Namespace] = seriesIdentity.Value },
            [new(policy.Target!.Kind,
                new Dictionary<string, string> { [issueIdentity.Namespace] = issueIdentity.Value },
                IssueLabel: issue.IssueLabel)]);
        return new(snapshot.Item.Title, work, [new(issue.RemoteId, issue.IssueLabel, issue.Title, issue.ExternalIds)]);
    }

    #endregion

    #region Actions - Preparation

    /// <inheritdoc />
    public async Task<ManagedWantedWork> PrepareAsync(ReviewedConnectedTarget reviewed, CancellationToken token) {
        var policy = ManagedFulfillmentPolicy.For(Kind);
        var seriesIdentity = policy.PinningIdentity(reviewed.Work.ExternalIds)
            ?? throw new ArgumentException($"The reviewed run needs {policy.IdentityDescription}.");
        var target = reviewed.Work.Targets is [{ } single] ? single
            : throw new ArgumentException("Request exactly one reviewed comic issue.");
        var issueIdentity = policy.TargetIdentityFormats[0].Find(target.ExternalIds)
            ?? throw new ArgumentException($"The reviewed issue needs {policy.IdentityDescription}.");
        var evidence = reviewed.Targets[0];
        var prepared = await writer.EnsureAsync(seriesIdentity, reviewed.Title, issueIdentity,
            evidence.Title, evidence.Label, token);
        return new(prepared.SeriesEntityId, [prepared.IssueEntityId], HasEveryFile: prepared.HasFile);
    }

    #endregion
}
