using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Stable content identity within one connected holding, with coordinates that must not silently change.</summary>
public sealed record ManagedTargetIdentity(string RemoteTargetId, EntityKind Kind, int? SeasonNumber, int? EpisodeNumber,
    int? AbsoluteNumber, string? IssueLabel = null);

/// <summary>Retained requested content identity, independent of whether its first source file exists yet.</summary>
public sealed record ManagedTargetBinding(ManagedTargetIdentity Target, Guid EntityId);

/// <summary>One remote target's established local identity and source-file evidence.</summary>
public sealed record ManagedEntityBinding(ManagedTargetIdentity Target, Guid EntityId, Guid SourceFileId);

/// <summary>Established ownership of one physical file, including every episode sharing its bytes.</summary>
public sealed record ManagedFileBinding(string RemoteFileId, string LocalPath, long SizeBytes, DateTimeOffset WrittenAt,
    bool IsAvailable, IReadOnlyList<ManagedEntityBinding> Entities);

/// <summary>Fresh mapped file evidence. Readable means local bytes can be opened and match the reported size, not that a provider supplied a content hash.</summary>
public sealed record ManagedObservedFile(string RemoteFileId, string LocalPath, long SizeBytes, DateTimeOffset WrittenAt,
    bool IsReadable, IReadOnlyList<ManagedTargetIdentity> Targets);

/// <summary>A source transition retaining its existing owners; null current evidence withdraws availability while keeping identities.</summary>
public sealed record ManagedSourceChange(ManagedFileBinding Previous, ManagedObservedFile? Current);

/// <summary>An all-or-nothing decision. A review reason always accompanies an empty change list.</summary>
public sealed record ManagedSourceReconciliationPlan(IReadOnlyList<ManagedSourceChange> Changes, string? ReviewReason);

/// <summary>Allows external replacements only when stable target identity and exact shared-file coverage remain intact.</summary>
public static class ManagedSourceReconciliation {
    /// <summary>Compares a fresh holding with established local ownership without mutating either. Unknown targets and changed coverage require explicit review.</summary>
    public static ManagedSourceReconciliationPlan Plan(IReadOnlyList<ManagedFileBinding> bindings, IReadOnlyList<ManagedObservedFile> observed) {
        var known = bindings.SelectMany(file => file.Entities).ToArray();
        if (known.Length == 0 || known.Select(owner => owner.Target.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != known.Length
            || known.Select(owner => owner.EntityId).Distinct().Count() != known.Length
            || known.Any(owner => owner.EntityId == Guid.Empty || owner.SourceFileId == Guid.Empty))
            return Review("The saved source bindings are incomplete or ambiguous.");
        var retainedTargets = known.Select(owner => owner.Target).ToArray();
        var scopedObserved = observed
            .Where(file => bindings.Any(binding => binding.RemoteFileId == file.RemoteFileId)
                || IntersectsEstablishedScope(retainedTargets, file.Targets))
            .ToArray();
        var targets = scopedObserved.SelectMany(file => file.Targets).ToArray();
        if (scopedObserved.Any(file => file.Targets.Count == 0)
            || scopedObserved.Select(file => file.RemoteFileId).Distinct(StringComparer.Ordinal).Count() != scopedObserved.Length
            || targets.Select(target => target.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != targets.Length)
            return Review("The connected library reports ambiguous file coverage.");
        var knownById = known.ToDictionary(owner => owner.Target.RemoteTargetId, StringComparer.Ordinal);
        foreach (var target in targets) {
            if (!knownById.TryGetValue(target.RemoteTargetId, out var owner))
                return Review("The connected holding has new targets. Link their local identities before expanding this tracked scope.");
            if (target != owner.Target) return Review("A tracked content identity or episode coordinate changed. Review its mapping before replacing files.");
        }
        var filesByTarget = scopedObserved.SelectMany(file => file.Targets.Select(target => (target.RemoteTargetId, File: file)))
            .ToDictionary(pair => pair.RemoteTargetId, pair => pair.File, StringComparer.Ordinal);
        var changes = new List<ManagedSourceChange>();
        foreach (var previous in bindings) {
            var coverage = previous.Entities.Select(owner => owner.Target.RemoteTargetId).ToHashSet(StringComparer.Ordinal);
            var currentFiles = coverage.Where(filesByTarget.ContainsKey).Select(id => filesByTarget[id]).DistinctBy(file => file.RemoteFileId).ToArray();
            if (currentFiles.Length > 1 || currentFiles.Length == 1 && !coverage.SetEquals(currentFiles[0].Targets.Select(target => target.RemoteTargetId)))
                return Review("The external app changed which targets share a file. Review this coverage before rebinding any source.");
            var current = currentFiles.SingleOrDefault();
            if (current?.IsReadable != true) {
                if (previous.IsAvailable) changes.Add(new(previous, null));
                continue;
            }
            if (!previous.IsAvailable || previous.RemoteFileId != current.RemoteFileId || previous.LocalPath != current.LocalPath
                || previous.SizeBytes != current.SizeBytes || previous.WrittenAt != current.WrittenAt)
                changes.Add(new(previous, current));
        }
        return new(changes, null);
    }

    /// <summary>
    /// Includes exact remote identities and plausible identity changes for retained content. A file
    /// that intersects the scope stays whole so shared bytes can never hide unowned coverage.
    /// </summary>
    public static bool IntersectsEstablishedScope(
        IReadOnlyList<ManagedTargetIdentity> retained,
        IReadOnlyList<ManagedTargetIdentity> observed) => observed.Any(candidate => retained.Any(saved =>
            saved.RemoteTargetId == candidate.RemoteTargetId || PlausiblySameCoordinates(saved, candidate)));

    private static bool PlausiblySameCoordinates(ManagedTargetIdentity saved, ManagedTargetIdentity candidate) {
        if (saved.Kind != candidate.Kind) return false;
        if (saved.Kind != EntityKind.VideoEpisode) return false;
        return saved.SeasonNumber is not null && saved.EpisodeNumber is not null
                && saved.SeasonNumber == candidate.SeasonNumber && saved.EpisodeNumber == candidate.EpisodeNumber
            || saved.AbsoluteNumber is not null && saved.AbsoluteNumber == candidate.AbsoluteNumber;
    }

    private static ManagedSourceReconciliationPlan Review(string reason) => new([], reason);
}
