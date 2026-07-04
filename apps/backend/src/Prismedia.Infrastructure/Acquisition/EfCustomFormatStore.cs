using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// EF-backed store for custom formats (named, scored release classifiers). Validates a format's shape on
/// save — non-empty name, at least one condition, a supported kind, and (for the regex condition types) a
/// pattern that actually compiles — so an unusable format is rejected with a clear 400 rather than silently
/// never matching.
/// </summary>
public sealed class EfCustomFormatStore(PrismediaDbContext db) : ICustomFormatStore {
    public async Task<IReadOnlyList<CustomFormatView>> ListAsync(CancellationToken cancellationToken) {
        var rows = await db.CustomFormats.AsNoTracking()
            .OrderBy(format => format.Kind)
            .ThenBy(format => format.Name)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToView).ToArray();
    }

    public async Task<CustomFormatView> SaveAsync(CustomFormatSaveRequest request, CancellationToken cancellationToken) {
        Validate(request);

        var now = DateTimeOffset.UtcNow;
        var row = request.Id is { } id
            ? await db.CustomFormats.FirstOrDefaultAsync(format => format.Id == id, cancellationToken)
            : null;

        if (row is null) {
            row = new CustomFormatRow {
                Id = request.Id ?? Guid.NewGuid(),
                CreatedAt = now
            };
            db.CustomFormats.Add(row);
        }

        row.Kind = request.Kind;
        row.Name = request.Name.Trim();
        row.ConditionsJson = CustomFormatConditionsJson.Serialize(ToConditions(request.Conditions));
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return ToView(row);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.CustomFormats.FirstOrDefaultAsync(format => format.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        db.CustomFormats.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Rejects an unusable format shape (empty name/conditions, wrong kind, or an uncompilable regex) with a 400-mapped error.</summary>
    private static void Validate(CustomFormatSaveRequest request) {
        if (string.IsNullOrWhiteSpace(request.Name)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "A custom format name is required.");
        }

        if (!AcquisitionProfileKinds.All.Contains(request.Kind)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "Custom formats support books, movies, TV series, and albums.");
        }

        if (request.Conditions.Count == 0) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "A custom format needs at least one condition.");
        }

        foreach (var condition in request.Conditions) {
            if (string.IsNullOrWhiteSpace(condition.Value)) {
                throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "Every custom format condition needs a value.");
            }

            // Regex condition types must carry a pattern that compiles; a bad pattern is caught here rather
            // than silently never matching at evaluation time.
            if (condition.Type is CustomFormatConditionType.ReleaseTitle or CustomFormatConditionType.ReleaseGroup) {
                try {
                    _ = new Regex(condition.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                } catch (ArgumentException) {
                    throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, $"The regular expression '{condition.Value}' is not valid.");
                }
            }
        }
    }

    private static IReadOnlyList<CustomFormatCondition> ToConditions(IReadOnlyList<CustomFormatConditionView> views) =>
        views.Select(view => new CustomFormatCondition(view.Type, view.Value.Trim(), view.Negate, view.Required)).ToArray();

    private static CustomFormatView ToView(CustomFormatRow row) =>
        new(
            row.Id,
            row.Kind,
            row.Name,
            CustomFormatConditionsJson.Deserialize(row.ConditionsJson)
                .Select(condition => new CustomFormatConditionView(condition.Type, condition.Value, condition.Negate, condition.Required))
                .ToArray());
}
