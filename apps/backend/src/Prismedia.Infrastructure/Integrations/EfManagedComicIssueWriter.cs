using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Persists the exact issue label beside the wanted entity created for a connected manager.</summary>
public sealed class EfManagedComicIssueWriter(IWantedEntityWriter wanted, PrismediaDbContext db) : IManagedComicIssueWriter {
    /// <inheritdoc />
    public async Task<(Guid SeriesEntityId, Guid IssueEntityId, bool HasFile)> EnsureAsync(
        ExternalIdentity seriesIdentity, string seriesTitle,
        ExternalIdentity issueIdentity, string issueTitle, string issueLabel,
        CancellationToken token) {
        if (ComicInstallmentNumber.Parse(issueLabel) is null)
            throw new ArgumentException("Choose an exact comic issue label.");
        var series = await wanted.EnsureAsync(EntityKind.ComicSeries, seriesIdentity,
            seriesTitle, null, matchTitleKindWide: false, token);
        var issue = await wanted.EnsureAsync(EntityKind.ComicInstallment, issueIdentity,
            issueTitle, series.EntityId, matchTitleKindWide: false, token);
        var detail = await db.ComicInstallmentDetails.FindAsync([issue.EntityId], token);
        if (detail is { InstallmentKind: not ComicInstallmentKind.Issue })
            throw new ArgumentException("The local installment has another release kind. Review its identity before requesting it.");
        if (detail is null)
            db.ComicInstallmentDetails.Add(new ComicInstallmentDetailRow {
                EntityId = issue.EntityId,
                InstallmentKind = ComicInstallmentKind.Issue
            });
        var existing = await db.EntityPositions.SingleOrDefaultAsync(row =>
            row.EntityId == issue.EntityId && row.Code == EntityPositionCodes.Chapter, token);
        if (existing is not null && existing.Label != issueLabel)
            throw new ArgumentException("The local issue has another exact label. Review its identity before requesting it.");
        if (existing is null) {
            var whole = issueLabel.Trim().Split('.', 2)[0];
            var position = int.TryParse(whole, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : 0;
            db.EntityPositions.Add(new EntityPositionRow { EntityId = issue.EntityId,
                Code = EntityPositionCodes.Chapter, Value = position, Label = issueLabel,
                UpdatedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync(token);
        return (series.EntityId, issue.EntityId, issue.HasFile);
    }
}
