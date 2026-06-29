using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for book acquisition profiles: decision rules, import target, and CRUD.</summary>
public sealed class EfBookAcquisitionProfileStore(PrismediaDbContext db) : IBookAcquisitionProfileStore {
    public async Task<BookAcquisitionRules> GetDefaultRulesAsync(CancellationToken cancellationToken) {
        var row = await DefaultRowAsync(cancellationToken);
        return row is null ? BookAcquisitionRules.Default : ToRules(row);
    }

    public async Task<BookImportProfile?> GetDefaultImportProfileAsync(CancellationToken cancellationToken) {
        var row = await DefaultRowAsync(cancellationToken);
        return row is null ? null : new BookImportProfile(row.TargetLibraryRootId, row.PathTemplate, row.ImportMode);
    }

    public async Task<bool> GetDefaultAutoPickAsync(CancellationToken cancellationToken) {
        var row = await DefaultRowAsync(cancellationToken);
        return row?.AutoPick ?? false;
    }

    public async Task<bool> GetDefaultAutoRedownloadAsync(CancellationToken cancellationToken) {
        var row = await DefaultRowAsync(cancellationToken);
        return row?.AutoRedownload ?? false;
    }

    public async Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken) {
        var rows = await db.BookAcquisitionProfiles
            .AsNoTracking()
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.DisplayName)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToView).ToArray();
    }

    public async Task<BookAcquisitionProfileView?> GetAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.BookAcquisitionProfiles.AsNoTracking().FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken);
        return row is null ? null : ToView(row);
    }

    public async Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveCommand command, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = command.Id is { } id
            ? await db.BookAcquisitionProfiles.FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken)
            : null;

        if (row is null) {
            row = new BookAcquisitionProfileRow {
                Id = command.Id ?? Guid.NewGuid(),
                CreatedAt = now
            };
            db.BookAcquisitionProfiles.Add(row);
        }

        var hasOthers = await db.BookAcquisitionProfiles.AnyAsync(profile => profile.Id != row.Id, cancellationToken);
        var shouldBeDefault = command.IsDefault || !hasOthers;

        row.DisplayName = command.DisplayName;
        row.TargetLibraryRootId = command.TargetLibraryRootId;
        row.PathTemplate = command.PathTemplate;
        row.ImportMode = command.ImportMode;
        row.AllowedFormats = command.AllowedFormats.Select(format => format.ToCode()).ToArray();
        row.Language = command.Language;
        row.MinSeeders = command.MinSeeders;
        row.MinSizeBytes = command.MinSizeBytes;
        row.MaxSizeBytes = command.MaxSizeBytes;
        row.RequiredTerms = command.RequiredTerms.ToArray();
        row.IgnoredTerms = command.IgnoredTerms.ToArray();
        row.PreferredTerms = command.PreferredTerms.ToArray();
        row.AutoPick = command.AutoPick;
        row.AutoRedownload = command.AutoRedownload;
        row.IsDefault = shouldBeDefault;
        row.UpdatedAt = now;

        if (shouldBeDefault) {
            var priorDefaults = await db.BookAcquisitionProfiles
                .Where(profile => profile.Id != row.Id && profile.IsDefault)
                .ToArrayAsync(cancellationToken);
            foreach (var priorDefault in priorDefaults) {
                priorDefault.IsDefault = false;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToView(row);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.BookAcquisitionProfiles.FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        var wasDefault = row.IsDefault;
        db.BookAcquisitionProfiles.Remove(row);
        await db.SaveChangesAsync(cancellationToken);

        if (wasDefault) {
            var replacement = await db.BookAcquisitionProfiles.OrderBy(profile => profile.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (replacement is not null) {
                replacement.IsDefault = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }

    private Task<BookAcquisitionProfileRow?> DefaultRowAsync(CancellationToken cancellationToken) =>
        db.BookAcquisitionProfiles
            .AsNoTracking()
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static BookAcquisitionRules ToRules(BookAcquisitionProfileRow row) =>
        new(
            row.AllowedFormats.Select(code => code.DecodeAs<BookFormat>()).ToArray(),
            row.Language,
            row.MinSeeders,
            row.MinSizeBytes,
            row.MaxSizeBytes,
            row.RequiredTerms,
            row.IgnoredTerms,
            row.PreferredTerms);

    private static BookAcquisitionProfileView ToView(BookAcquisitionProfileRow row) =>
        new(
            row.Id,
            row.DisplayName,
            row.IsDefault,
            row.TargetLibraryRootId,
            row.PathTemplate,
            row.ImportMode,
            row.AllowedFormats.Select(code => code.DecodeAs<BookFormat>()).ToArray(),
            row.Language,
            row.MinSeeders,
            row.MinSizeBytes,
            row.MaxSizeBytes,
            row.RequiredTerms,
            row.IgnoredTerms,
            row.PreferredTerms,
            row.AutoPick,
            row.AutoRedownload);
}
