using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Creates or reuses one wanted Comic Vine series and issue within a reviewed transaction.</summary>
public interface IManagedComicIssueWriter {
    /// <summary>Preserves existing entities and exact issue position; a retained source cannot become a new manager request.</summary>
    Task<(Guid SeriesEntityId, Guid IssueEntityId, bool HasFile)> EnsureAsync(
        ExternalIdentity seriesIdentity, string seriesTitle,
        ExternalIdentity issueIdentity, string issueTitle, string issueLabel,
        CancellationToken token);
}
