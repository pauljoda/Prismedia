namespace Prismedia.Application.Acquisition;

/// <summary>Remembers the last held attempt examined so bounded recovery sweeps fairly revisit a backlog.</summary>
public sealed class HeldTvImportRecoveryCursor {
    private Position? last;

    /// <summary>Orders held attempts for the next recovery sweep.</summary>
    public IReadOnlyList<HeldTvImport> Order(IReadOnlyList<HeldTvImport> held) {
        var observed = Volatile.Read(ref last);
        var ordered = held.OrderBy(attempt => attempt.HeldAt).ThenBy(attempt => attempt.Id).ToArray();
        if (observed is null) return ordered;
        // Keep the timestamp as well as the id: a resumed/deleted attempt may vanish between scopes.
        return ordered.Where(attempt => IsAfter(attempt, observed))
            .Concat(ordered.Where(attempt => !IsAfter(attempt, observed))).ToArray();
    }

    /// <summary>Records an examined attempt, including one that remains held or fails its lookup.</summary>
    public void Advance(HeldTvImport held) => Volatile.Write(ref last, new(held.HeldAt, held.Id));

    private static bool IsAfter(HeldTvImport attempt, Position position) => attempt.HeldAt > position.HeldAt
        || attempt.HeldAt == position.HeldAt && attempt.Id.CompareTo(position.Id) > 0;

    private sealed record Position(DateTimeOffset HeldAt, Guid Id);
}
