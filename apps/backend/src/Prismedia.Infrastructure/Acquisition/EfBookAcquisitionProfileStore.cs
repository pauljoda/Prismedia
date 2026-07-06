using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for acquisition profiles (kind-scoped): decision rules, import target, and CRUD.</summary>
public sealed class EfBookAcquisitionProfileStore(PrismediaDbContext db) : IBookAcquisitionProfileStore {
    public async Task<BookAcquisitionRules> GetRulesAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) {
        var row = await ResolveRowAsync(profileId, kind, cancellationToken);
        if (row is null) {
            return BookAcquisitionRules.Default;
        }

        return ToRules(row, await ResolveCustomFormatsAsync(row, cancellationToken));
    }

    public async Task<BookImportProfile?> GetImportProfileAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) {
        var row = await ResolveRowAsync(profileId, kind, cancellationToken);
        return row is null ? null : new BookImportProfile(row.TargetLibraryRootId, row.PathTemplate, row.ImportMode);
    }

    public async Task<bool> GetAutoPickAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) {
        var row = await ResolveRowAsync(profileId, kind, cancellationToken);
        return row?.AutoPick ?? false;
    }

    public async Task<bool> GetAutoRedownloadAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) {
        var row = await ResolveRowAsync(profileId, kind, cancellationToken);
        return row?.AutoRedownload ?? false;
    }

    public async Task<string?> GetDownloadCategoryAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) {
        var row = await ResolveRowAsync(profileId, kind, cancellationToken);
        return string.IsNullOrWhiteSpace(row?.DownloadCategory) ? null : row.DownloadCategory;
    }

    public async Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken) {
        var rows = await db.BookAcquisitionProfiles
            .AsNoTracking()
            .OrderBy(profile => profile.Kind)
            .ThenByDescending(profile => profile.IsDefault)
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

        // IsDefault is per kind: the first profile of a kind is that kind's default automatically.
        var hasOthers = await db.BookAcquisitionProfiles.AnyAsync(
            profile => profile.Id != row.Id && profile.Kind == command.Kind, cancellationToken);
        var shouldBeDefault = command.IsDefault || !hasOthers;

        row.Kind = command.Kind;
        row.DisplayName = command.DisplayName;
        row.TargetLibraryRootId = command.TargetLibraryRootId;
        row.PathTemplate = command.PathTemplate;
        row.ImportMode = command.ImportMode;
        row.AllowedFormats = command.AllowedFormats.Select(format => format.ToCode()).ToArray();
        row.AllowedQualities = (command.AllowedQualities ?? []).ToArray();
        row.CutoffQuality = string.IsNullOrWhiteSpace(command.CutoffQuality) ? null : command.CutoffQuality;
        row.PreferredLanguages = command.PreferredLanguages.ToArray();
        row.MinSeeders = command.MinSeeders;
        row.MinSizeBytes = command.MinSizeBytes;
        row.MaxSizeBytes = command.MaxSizeBytes;
        row.RequiredTerms = command.RequiredTerms.ToArray();
        row.IgnoredTerms = command.IgnoredTerms.ToArray();
        row.PreferredTerms = command.PreferredTerms.ToArray();
        row.WeightedTermsJson = JsonSerializer.Serialize(command.WeightedTerms);
        row.FormatScoresJson = JsonSerializer.Serialize(command.FormatScores ?? new Dictionary<string, int>());
        row.MinFormatScore = command.MinFormatScore;
        row.CutoffFormatScore = command.CutoffFormatScore;
        row.AutoPick = command.AutoPick;
        row.AutoRedownload = command.AutoRedownload;
        row.UpgradeUntilCutoff = command.UpgradeUntilCutoff;
        row.CutoffSourceTier = command.CutoffSourceTier;
        row.CutoffFormatTier = command.CutoffFormatTier;
        row.DownloadCategory = string.IsNullOrWhiteSpace(command.DownloadCategory) ? null : command.DownloadCategory.Trim();
        row.IsDefault = shouldBeDefault;
        row.UpdatedAt = now;

        if (shouldBeDefault) {
            var priorDefaults = await db.BookAcquisitionProfiles
                .Where(profile => profile.Id != row.Id && profile.IsDefault && profile.Kind == command.Kind)
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
        var kind = row.Kind;
        db.BookAcquisitionProfiles.Remove(row);
        await db.SaveChangesAsync(cancellationToken);

        if (wasDefault) {
            var replacement = await db.BookAcquisitionProfiles
                .Where(profile => profile.Kind == kind)
                .OrderBy(profile => profile.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (replacement is not null) {
                replacement.IsDefault = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the effective profile row: an explicit id wins when it exists and is the right kind,
    /// else the kind's default profile, else any profile of the kind — a stale or wrong-kind request
    /// choice degrades instead of failing the search or import. Acquisition kinds normalize to their
    /// governing profile kind here (a season-pack or episode acquisition resolves the TV profile), so
    /// no caller translates kinds itself.
    /// </summary>
    private async Task<BookAcquisitionProfileRow?> ResolveRowAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) {
        kind = AcquisitionProfileKinds.For(kind);
        if (profileId is { } id) {
            var chosen = await db.BookAcquisitionProfiles.AsNoTracking()
                .FirstOrDefaultAsync(profile => profile.Id == id && profile.Kind == kind, cancellationToken);
            if (chosen is not null) {
                return chosen;
            }
        }

        return await db.BookAcquisitionProfiles
            .AsNoTracking()
            .Where(profile => profile.Kind == kind)
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static BookAcquisitionRules ToRules(BookAcquisitionProfileRow row, IReadOnlyList<ScoredCustomFormat> customFormats) =>
        new(
            row.AllowedFormats.Select(code => code.DecodeAs<BookFormat>()).ToArray(),
            row.PreferredLanguages,
            row.MinSeeders,
            row.MinSizeBytes,
            row.MaxSizeBytes,
            row.RequiredTerms,
            row.IgnoredTerms,
            row.PreferredTerms,
            DecodeWeightedTerms(row.WeightedTermsJson)) {
            Kind = row.Kind,
            AllowedQualities = row.AllowedQualities,
            CutoffQuality = row.CutoffQuality,
            CustomFormats = customFormats,
            MinFormatScore = row.MinFormatScore,
            CutoffFormatScore = row.CutoffFormatScore
        };

    /// <summary>
    /// Resolves a profile's format-score map against the custom-format table (one query, joined here):
    /// every custom format of the profile's kind with a non-zero score in the profile's map becomes a
    /// <see cref="ScoredCustomFormat"/> carrying its decoded conditions. Formats scored 0, missing from the
    /// map, or of another kind are skipped, so the rules carry only the formats that actually matter.
    /// </summary>
    private async Task<IReadOnlyList<ScoredCustomFormat>> ResolveCustomFormatsAsync(BookAcquisitionProfileRow row, CancellationToken cancellationToken) {
        var scores = DecodeFormatScores(row.FormatScoresJson);
        if (scores.Count == 0) {
            return [];
        }

        var formats = await db.CustomFormats.AsNoTracking()
            .Where(format => format.Kind == row.Kind)
            .ToArrayAsync(cancellationToken);

        var resolved = new List<ScoredCustomFormat>(formats.Length);
        foreach (var format in formats) {
            if (scores.TryGetValue(format.Id.ToString(), out var score) && score != 0) {
                resolved.Add(new ScoredCustomFormat(format.Name, score, CustomFormatConditionsJson.Deserialize(format.ConditionsJson)));
            }
        }

        return resolved;
    }

    private static BookAcquisitionProfileView ToView(BookAcquisitionProfileRow row) =>
        new(
            row.Id,
            row.Kind,
            row.DisplayName,
            row.IsDefault,
            row.TargetLibraryRootId,
            row.PathTemplate,
            row.ImportMode,
            row.AllowedFormats.Select(code => code.DecodeAs<BookFormat>()).ToArray(),
            row.PreferredLanguages,
            row.MinSeeders,
            row.MinSizeBytes,
            row.MaxSizeBytes,
            row.RequiredTerms,
            row.IgnoredTerms,
            row.PreferredTerms,
            DecodeWeightedTerms(row.WeightedTermsJson),
            row.AutoPick,
            row.AutoRedownload,
            row.UpgradeUntilCutoff,
            row.CutoffSourceTier,
            row.CutoffFormatTier,
            row.DownloadCategory,
            row.AllowedQualities,
            row.CutoffQuality,
            DecodeFormatScores(row.FormatScoresJson),
            row.MinFormatScore,
            row.CutoffFormatScore);

    private static IReadOnlyList<WeightedTerm> DecodeWeightedTerms(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<WeightedTerm[]>(json) ?? [];
        } catch (JsonException) {
            return [];
        }
    }

    /// <summary>Decodes the profile's format-id → score map; a malformed blob resolves to no scores rather than throwing.</summary>
    private static IReadOnlyDictionary<string, int> DecodeFormatScores(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return new Dictionary<string, int>();
        }

        try {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
        } catch (JsonException) {
            return new Dictionary<string, int>();
        }
    }
}
