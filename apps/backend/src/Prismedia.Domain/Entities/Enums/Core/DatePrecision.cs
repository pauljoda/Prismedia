namespace Prismedia.Domain.Entities;

/// <summary>
/// Granularity of a metadata date value, stored on the dates capability alongside the
/// day-pinned sortable value. Providers send anything from a bare year (MusicBrainz
/// life-spans, MangaDex publication years) to a full timestamp (YouTube publish dates);
/// the precision records how much of the sortable value is meaningful.
/// </summary>
public enum DatePrecision {
    /// <summary>Full calendar date; finer timestamp input is truncated to its day.</summary>
    [Code("day")]
    Day,

    /// <summary>Year and month only; the sortable value is pinned to the first of the month.</summary>
    [Code("month")]
    Month,

    /// <summary>Year only; the sortable value is pinned to January 1st.</summary>
    [Code("year")]
    Year
}
