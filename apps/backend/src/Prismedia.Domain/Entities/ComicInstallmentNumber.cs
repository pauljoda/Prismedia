using System.Text.RegularExpressions;

namespace Prismedia.Domain.Entities;

/// <summary>
/// A comparable comic issue or chapter designation, independent of its integer shelf position.
/// Numeric padding is insignificant; fractions, suffixes, signs, and ranges remain distinct.
/// The original display spelling belongs in the position label, not this matching value.
/// </summary>
public sealed partial record ComicInstallmentNumber {
    private ComicInstallmentNumber(string value) => Value = value;

    /// <summary>Normalized designation used for exact matching; never a rounded integer.</summary>
    public string Value { get; }

    /// <summary>Normalizes a nonempty bounded designation without inferring edition or variant identity.</summary>
    public static ComicInstallmentNumber? Parse(string? label) {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 128) return null;
        var value = label.Trim().ToUpperInvariant();
        var match = NumericDesignation().Match(value);
        if (!match.Success) return new(value);
        var whole = match.Groups["whole"].Value.TrimStart('0');
        if (whole.Length == 0) whole = "0";
        var fraction = match.Groups["fraction"].Value.TrimEnd('0');
        var sign = match.Groups["sign"].Value;
        if (whole == "0" && fraction.Length == 0) sign = string.Empty;
        return new(sign + whole + (fraction.Length == 0 ? string.Empty : "." + fraction) + match.Groups["suffix"].Value);
    }

    [GeneratedRegex(@"\A(?<sign>-?)(?<whole>[0-9]+)(?:\.(?<fraction>[0-9]+))?(?<suffix>[A-Z]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDesignation();
}
