using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Application use case for listing, saving, and deleting book acquisition profiles.</summary>
public sealed class BookAcquisitionProfileCommandService(IBookAcquisitionProfileStore store) {
    public Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveRequest request, CancellationToken cancellationToken) =>
        store.SaveAsync(ToCommand(request), cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    private static BookAcquisitionProfileSaveCommand ToCommand(BookAcquisitionProfileSaveRequest request) {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "A display name is required.");
        }

        if (request.TargetLibraryRootId == Guid.Empty) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "A target library root is required.");
        }

        if (request.MinSeeders < 0) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "Minimum seeders cannot be negative.");
        }

        if (!AcquisitionProfileKinds.All.Contains(request.Kind)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "Profiles support books, movies, TV series, and albums.");
        }

        var pathTemplate = ResolvePathTemplate(request.Kind, request.PathTemplate);

        return new BookAcquisitionProfileSaveCommand(
            request.Id,
            request.DisplayName.Trim(),
            request.IsDefault,
            request.Kind,
            request.TargetLibraryRootId,
            pathTemplate,
            request.ImportMode,
            request.AllowedFormats.Distinct().ToArray(),
            request.PreferredLanguages
                .Where(language => !string.IsNullOrWhiteSpace(language))
                .Select(language => language.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            request.MinSeeders,
            request.MinSizeBytes,
            request.MaxSizeBytes,
            request.RequiredTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()).ToArray(),
            request.IgnoredTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()).ToArray(),
            request.PreferredTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()).ToArray(),
            request.WeightedTerms
                .Where(term => !string.IsNullOrWhiteSpace(term.Term) && term.Weight != 0)
                .Select(term => new WeightedTerm(term.Term.Trim(), Math.Clamp(term.Weight, -10_000, 10_000)))
                .ToArray(),
            request.AutoPick,
            request.AutoRedownload,
            request.UpgradeUntilCutoff,
            request.CutoffSourceTier,
            request.CutoffFormatTier,
            request.DownloadCategory,
            (request.AllowedQualities ?? []).Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.Ordinal).ToArray(),
            request.CutoffQuality,
            // Per-format scores are clamped like weighted terms (±10_000); a 0 score means "not scored" and
            // is dropped so it is never carried onto the rules. Malformed ids are the store's concern (a
            // score keyed by a non-existent format simply resolves to nothing).
            (request.FormatScores ?? new Dictionary<string, int>())
                .Where(entry => entry.Value != 0)
                .ToDictionary(entry => entry.Key, entry => Math.Clamp(entry.Value, -10_000, 10_000)),
            Math.Clamp(request.MinFormatScore, -10_000, 10_000),
            request.CutoffFormatScore is { } cutoff ? Math.Clamp(cutoff, -10_000, 10_000) : null);
    }

    /// <summary>
    /// Resolves and validates the path template a profile will store. A media kind (movie, TV, music)
    /// stores its kind default when the incoming template is blank and validates a non-blank template with
    /// <see cref="MediaNamingTemplates.Validate"/>; a book keeps its existing "a template is required" rule
    /// (its own renderer needs no structural validation).
    /// </summary>
    private static string ResolvePathTemplate(EntityKind kind, string pathTemplate) {
        if (MediaNamingTemplates.IsMediaKind(kind)) {
            if (string.IsNullOrWhiteSpace(pathTemplate)) {
                return MediaNamingTemplates.DefaultFor(kind)!;
            }

            var trimmed = pathTemplate.Trim();
            var error = MediaNamingTemplates.Validate(kind, trimmed);
            if (error is not null) {
                throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, error);
            }

            return trimmed;
        }

        if (string.IsNullOrWhiteSpace(pathTemplate)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "A path template is required.");
        }

        return pathTemplate.Trim();
    }
}
