namespace Prismedia.Domain.Entities;

/// <summary>
/// Derives the identify-proposal vocabulary from every discovered <see cref="EntityKindDefinition"/>
/// plus the single proposal-only leaf-episode token. Adding an Entity kind therefore cannot omit
/// it from proposal decoding, serialization, schema generation, or frontend code generation.
/// </summary>
public sealed class ProposalKindCodec : ICodec<ProposalKind> {
    private static readonly IReadOnlyList<string> KnownCodes = Array.AsReadOnly(
        EntityKindRegistry.All
            .Select(definition => definition.Code)
            .Append(ProposalKind.VideoEpisodeCode)
            .ToArray());

    /// <inheritdoc />
    public Type ValueType => typeof(ProposalKind);

    /// <inheritdoc />
    public IReadOnlyList<string> Codes => KnownCodes;

    /// <inheritdoc />
    public string Encode(ProposalKind value) {
        if (value.IsVideoEpisode) {
            return ProposalKind.VideoEpisodeCode;
        }

        if (value.TryGetEntityKind(out var kind)) {
            return EntityKindRegistry.ToCode(kind);
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported ProposalKind value.");
    }

    /// <inheritdoc />
    public ProposalKind Decode(string code) =>
        TryDecode(code, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported ProposalKind code.");

    /// <inheritdoc />
    public bool TryDecode(string code, out ProposalKind value) {
        if (string.IsNullOrWhiteSpace(code)) {
            value = default;
            return false;
        }

        var normalized = code.Trim();
        if (normalized.Equals(ProposalKind.VideoEpisodeCode, StringComparison.OrdinalIgnoreCase)) {
            value = ProposalKind.VideoEpisode;
            return true;
        }

        if (EntityKindRegistry.TryGet(normalized, out var entityKind)) {
            value = entityKind;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public string EncodeObject(object value) =>
        value is ProposalKind kind
            ? Encode(kind)
            : throw new ArgumentException("Expected ProposalKind.", nameof(value));

    /// <inheritdoc />
    public object DecodeObject(string code) => Decode(code);
}
