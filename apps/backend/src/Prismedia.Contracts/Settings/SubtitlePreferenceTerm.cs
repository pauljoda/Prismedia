using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Settings;

/// <summary>
/// One case-insensitive subtitle-selection term and the score it contributes when a track's
/// language code or display label contains the term.
/// </summary>
/// <param name="Term">Text matched independently against a subtitle track.</param>
/// <param name="Weight">Positive score added once when the term matches.</param>
public sealed record SubtitlePreferenceTerm(
    [property: JsonPropertyName("term")] string Term,
    [property: JsonPropertyName("weight")] int Weight);
