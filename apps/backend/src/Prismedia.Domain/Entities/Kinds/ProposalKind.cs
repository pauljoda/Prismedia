namespace Prismedia.Domain.Entities;

/// <summary>
/// Identify-proposal target kind. Every persisted <see cref="EntityKind"/> is accepted through
/// implicit conversion, so this vocabulary grows automatically with discovered Entity kinds.
/// </summary>
public readonly record struct ProposalKind {
    private const byte EntityDiscriminator = 1;

    private readonly EntityKind _entityKind;
    private readonly byte _discriminator;

    private ProposalKind(EntityKind entityKind, byte discriminator) {
        _entityKind = entityKind;
        _discriminator = discriminator;
    }

    /// <summary>Directly playable episodic video kind.</summary>
    public static ProposalKind VideoEpisode { get; } =
        new(EntityKind.VideoEpisode, EntityDiscriminator);

    /// <summary>Lifts any persisted Entity kind into the proposal vocabulary.</summary>
    public static implicit operator ProposalKind(EntityKind kind) =>
        new(kind, EntityDiscriminator);

    /// <summary>Returns the Entity kind Prismedia persists for this proposal target.</summary>
    /// <exception cref="InvalidOperationException">The value is an uninitialized default.</exception>
    public EntityKind ToPersistedEntityKind() => _discriminator == EntityDiscriminator
        ? _entityKind
        : throw new InvalidOperationException("An uninitialized ProposalKind has no persisted Entity kind.");

    /// <summary>Attempts to expose the entity-backed proposal kind.</summary>
    internal bool TryGetEntityKind(out EntityKind kind) {
        if (_discriminator == EntityDiscriminator) {
            kind = _entityKind;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>Whether this value was constructed through a supported path.</summary>
    internal bool IsValid => _discriminator == EntityDiscriminator;

    /// <inheritdoc />
    public override string ToString() => IsValid ? this.ToCode() : nameof(ProposalKind);
}
