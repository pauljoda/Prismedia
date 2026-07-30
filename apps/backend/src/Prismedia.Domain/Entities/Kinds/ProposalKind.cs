namespace Prismedia.Domain.Entities;

/// <summary>
/// Identify-proposal target kind. Every persisted <see cref="EntityKind"/> is accepted through
/// implicit conversion, so this vocabulary grows automatically with discovered Entity kinds. The
/// only proposal-only value is <see cref="VideoEpisode"/>, which distinguishes a provider leaf
/// episode while still persisting as <see cref="EntityKind.Video"/>.
/// </summary>
public readonly record struct ProposalKind {
    /// <summary>Stable proposal-only code for a leaf episode in the identify protocol.</summary>
    public const string VideoEpisodeCode = "video-episode";

    private const byte EntityDiscriminator = 1;
    private const byte VideoEpisodeDiscriminator = 2;

    private readonly EntityKind _entityKind;
    private readonly byte _discriminator;

    private ProposalKind(EntityKind entityKind, byte discriminator) {
        _entityKind = entityKind;
        _discriminator = discriminator;
    }

    /// <summary>Provider-only leaf episode kind, persisted by Prismedia as a Video entity.</summary>
    public static ProposalKind VideoEpisode { get; } =
        new(EntityKind.Video, VideoEpisodeDiscriminator);

    /// <summary>Lifts any persisted Entity kind into the proposal vocabulary.</summary>
    public static implicit operator ProposalKind(EntityKind kind) =>
        new(kind, EntityDiscriminator);

    /// <summary>Returns the Entity kind Prismedia persists for this proposal target.</summary>
    /// <exception cref="InvalidOperationException">The value is an uninitialized default.</exception>
    public EntityKind ToPersistedEntityKind() => _discriminator switch {
        EntityDiscriminator => _entityKind,
        VideoEpisodeDiscriminator => EntityKind.Video,
        _ => throw new InvalidOperationException("An uninitialized ProposalKind has no persisted Entity kind.")
    };

    /// <summary>Attempts to expose an entity-backed proposal kind without collapsing protocol-only values.</summary>
    internal bool TryGetEntityKind(out EntityKind kind) {
        if (_discriminator == EntityDiscriminator) {
            kind = _entityKind;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>Whether this is the proposal-only leaf-episode token.</summary>
    internal bool IsVideoEpisode => _discriminator == VideoEpisodeDiscriminator;

    /// <summary>Whether this value was constructed through a supported path.</summary>
    internal bool IsValid => _discriminator is EntityDiscriminator or VideoEpisodeDiscriminator;

    /// <inheritdoc />
    public override string ToString() => IsValid ? this.ToCode() : nameof(ProposalKind);
}
