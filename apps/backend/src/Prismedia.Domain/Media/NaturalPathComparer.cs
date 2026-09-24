namespace Prismedia.Domain.Media;

/// <summary>
/// Orders file paths the way people read numbered file names: digit runs compare by value
/// ("Part 2" before "Part 10"), everything else case-insensitively, and equal values fall back to the
/// shorter digit run and then the shorter path so the order is total and stable.
/// </summary>
public sealed class NaturalPathComparer : IComparer<string> {
    #region Static Variables

    /// <summary>The shared stateless instance.</summary>
    public static readonly NaturalPathComparer Instance = new();

    #endregion

    #region Constructors

    private NaturalPathComparer() {
    }

    #endregion

    #region Actions - Comparison

    /// <inheritdoc />
    public int Compare(string? x, string? y) {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var ix = 0;
        var iy = 0;
        while (ix < x.Length && iy < y.Length) {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy])) {
                var numberCompare = CompareNumber(x, ref ix, y, ref iy);
                if (numberCompare != 0) return numberCompare;
                continue;
            }

            var charCompare = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
            if (charCompare != 0) return charCompare;
            ix++;
            iy++;
        }

        return x.Length.CompareTo(y.Length);
    }

    private static int CompareNumber(string x, ref int ix, string y, ref int iy) {
        var startX = ix;
        var startY = iy;
        while (ix < x.Length && char.IsDigit(x[ix])) ix++;
        while (iy < y.Length && char.IsDigit(y[iy])) iy++;

        var spanX = x.AsSpan(startX, ix - startX).TrimStart('0');
        var spanY = y.AsSpan(startY, iy - startY).TrimStart('0');
        if (spanX.Length != spanY.Length) return spanX.Length.CompareTo(spanY.Length);

        var digitCompare = spanX.CompareTo(spanY, StringComparison.Ordinal);
        return digitCompare != 0 ? digitCompare : (ix - startX).CompareTo(iy - startY);
    }

    #endregion
}
