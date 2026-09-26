using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities.Mappers.Capabilities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Reads the two progresses of unfinished Books that keep reading and listening Separate, for list and
/// detail projections. Each share comes from that format's own exact checkpoint through the Book's
/// alignment, so listening is never shown as reading progress.
/// </summary>
internal static class SeparateBookProgressReader {
    #region Actions - Loading

    /// <summary>
    /// Loads separate progress for the Books among <paramref name="bookIds"/> that have both formats, are
    /// Separate, are unfinished, and hold at least one checkpoint of <paramref name="userId"/>. Books
    /// without checkpoints cost one query; the alignments load only for Books with audio.
    /// </summary>
    /// <param name="db">Database context.</param>
    /// <param name="userId">The viewing user; an empty id yields nothing.</param>
    /// <param name="bookIds">Candidate Book ids (other kinds are ignored).</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Separate progress keyed by Book id.</returns>
    public static async Task<IReadOnlyDictionary<Guid, SeparateProgress>> LoadAsync(
        PrismediaDbContext db,
        Guid userId,
        IReadOnlyCollection<Guid> bookIds,
        CancellationToken cancellationToken) {
        var none = new Dictionary<Guid, SeparateProgress>();
        if (userId == Guid.Empty || bookIds.Count == 0) {
            return none;
        }

        var checkpointRows = await db.UserProgressCheckpoints.AsNoTracking()
            .Where(row => row.UserId == userId && bookIds.Contains(row.EntityId))
            .ToArrayAsync(cancellationToken);
        if (checkpointRows.Length == 0) {
            return none;
        }

        var started = checkpointRows.Select(row => row.EntityId).Distinct().ToArray();
        var finished = await db.UserEntityStates.AsNoTracking()
            .Where(state => state.UserId == userId && started.Contains(state.EntityId) && state.ProgressCompletedAt != null)
            .Select(state => state.EntityId)
            .ToArrayAsync(cancellationToken);
        var unfinished = started.Except(finished).Select(id => (Guid?)id).ToArray();
        var trackKind = EntityKind.AudioTrack.ToCode();
        var withAudio = await db.Entities.AsNoTracking()
            .Where(track => unfinished.Contains(track.ParentEntityId) && track.KindCode == trackKind && !track.IsWanted &&
                db.EntityFiles.Any(file => file.EntityId == track.Id && file.Role == EntityFileRole.Source))
            .Select(track => track.ParentEntityId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (withAudio.Length == 0) {
            return none;
        }

        var alignments = await EfWorkAlignmentReader.LoadPersistedManyAsync(db, withAudio, cancellationToken);
        var checkpointsByBook = checkpointRows.ToLookup(row => row.EntityId);
        var result = new Dictionary<Guid, SeparateProgress>();
        foreach (var (bookId, alignment) in alignments) {
            if (!alignment.KeepsProgressSeparate || alignment.Link.Reason is not { } reason) {
                continue;
            }

            var checkpoints = checkpointsByBook[bookId]
                .Select(row => ProgressCapabilityMapper.DecodeCheckpoint(ConsumptionModalityDefinition.For(row.Modality), row))
                .OfType<ProgressCheckpoint>()
                .ToArray();
            var reading = checkpoints.FirstOrDefault(checkpoint => !checkpoint.Definition.AddressesByOffset);
            var listening = checkpoints.FirstOrDefault(checkpoint => checkpoint.Definition.AddressesByOffset);
            result[bookId] = new SeparateProgress(
                reason,
                reading is null ? null : alignment.ReadingFraction(reading),
                listening is null ? null : alignment.ListeningFraction(listening));
        }
        return result;
    }

    #endregion
}
