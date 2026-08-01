using Prismedia.Application.Security;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>
/// Hydrates and persists the playback capability against the current user's
/// <c>user_entity_states</c> row. Without an authenticated user (worker, system
/// context) the capability hydrates empty and persists nothing — playback state is a
/// user opinion, never a system fact.
/// </summary>
internal sealed class PlaybackCapabilityMapper(PrismediaDbContext db, ICurrentUserContext currentUser) :
    IEntityCapabilityMapper,
    IEntityMutableStateMapper<CapabilityPlayback> {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty) {
            return;
        }

        var row = await UserEntityStateColumns.FindAsync(db, userId, entity.Id, cancellationToken);
        if (row is null || !UserEntityStateColumns.HasPlayback(row)) {
            return;
        }

        entity.RemoveCapability<CapabilityPlayback>();
        entity.AddCapability(new CapabilityPlayback(new CapabilityPlayback.State(
            row.PlayCount,
            row.SkipCount,
            TimeSpan.FromSeconds(row.PlayDurationSeconds),
            TimeSpan.FromSeconds(row.ResumeSeconds),
            row.LastPlayedAt,
            row.CompletedAt)));
    }

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty || entity.PlaybackCapability is not { Value: { } playback }) {
            return;
        }

        // Playable kinds start with an empty default capability. Saving unrelated entity fields or
        // markers must not manufacture a user-state row just because that default exists.
        if (IsEmpty(playback)) {
            return;
        }

        var row = await UserEntityStateColumns.GetOrAddAsync(db, userId, entity.Id, cancellationToken);
        row.PlayCount = playback.PlayCount;
        row.SkipCount = playback.SkipCount;
        row.PlayDurationSeconds = playback.PlayDuration.TotalSeconds;
        row.ResumeSeconds = playback.ResumeTime.TotalSeconds;
        row.LastPlayedAt = playback.LastPlayedAt;
        row.CompletedAt = playback.CompletedAt;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsEmpty(CapabilityPlayback.State playback) =>
        playback.PlayCount == 0 &&
        playback.SkipCount == 0 &&
        playback.PlayDuration == TimeSpan.Zero &&
        playback.ResumeTime == TimeSpan.Zero &&
        playback.LastPlayedAt is null &&
        playback.CompletedAt is null;
}
