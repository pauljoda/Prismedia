using System.Globalization;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Parsed form of one provider- or user-supplied date string: the normalized display value,
/// the day-pinned sortable date, and how much of that date is meaningful.
/// </summary>
/// <param name="NormalizedValue">Canonical display text for the parsed precision (yyyy, yyyy-MM, or yyyy-MM-dd).</param>
/// <param name="SortableValue">Sortable date pinned to the earliest day the value can mean.</param>
/// <param name="Precision">Granularity actually carried by the original value.</param>
public sealed record ParsedEntityDate(string NormalizedValue, DateOnly SortableValue, DatePrecision Precision);

/// <summary>
/// Parses the date strings metadata providers and users supply for the dates capability.
/// Accepts full dates, bare years, year-months, and ISO timestamps; anything else is not a
/// date and yields null so callers can decide between rejecting and storing as-is unsorted.
/// </summary>
public static class EntityDateParser {
    /// <summary>
    /// Parses one date value into its normalized, sortable, precision-tagged form.
    /// </summary>
    /// <param name="value">Raw date text from a patch, plugin proposal, or user edit.</param>
    /// <returns>The parsed date, or null when the value is not a recognizable date.</returns>
    public static ParsedEntityDate? Parse(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var trimmed = value.Trim();

        if (trimmed.Length == 4 &&
            int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
            year is >= 1 and <= 9999) {
            return new ParsedEntityDate(trimmed, new DateOnly(year, 1, 1), DatePrecision.Year);
        }

        if (trimmed.Length == 7 && trimmed[4] == '-' &&
            DateOnly.TryParseExact($"{trimmed}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) {
            return new ParsedEntityDate(trimmed, month, DatePrecision.Month);
        }

        if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, out var date)) {
            return new ParsedEntityDate(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), date, DatePrecision.Day);
        }

        // Timestamps keep the calendar day as written in their own offset rather than
        // shifting through UTC, so a late-evening publish date does not move a day.
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) {
            var day = DateOnly.FromDateTime(timestamp.Date);
            return new ParsedEntityDate(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), day, DatePrecision.Day);
        }

        return null;
    }
}
