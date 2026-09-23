using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>An administrator's explicit association between a remote target and its already scanned local source.</summary>
public sealed record ManagedBindingSelection(string RemoteTargetId, Guid EntityId, Guid SourceFileId);

/// <summary>Local source ownership and numbering observed independently of the connected application.</summary>
public sealed record ManagedLocalSource(Guid EntityId, Guid SourceFileId, string LocalPath, EntityKind Kind,
    int? SeasonNumber, int? EpisodeNumber, int? AbsoluteNumber, string? IssueLabel = null, Guid? ParentEntityId = null);

/// <summary>Exact source bindings, or one reason that the complete selection needs review.</summary>
public sealed record ManagedSourceAdoptionPlan(IReadOnlyList<ManagedFileBinding> Bindings, string? ReviewReason);

/// <summary>Establishes connected ownership only when remote coverage and the explicitly selected local owners agree.</summary>
public static class ManagedSourceAdoption {
    /// <summary>Validates the complete readable scope without inventing entities or inferring matches from titles.</summary>
    public static ManagedSourceAdoptionPlan Plan(IReadOnlyList<ManagedObservedFile> observed,
        IReadOnlyList<ManagedBindingSelection> selected, IReadOnlyList<ManagedLocalSource> local) {
        var targets = observed.SelectMany(file => file.Targets).ToArray();
        if (observed.Count == 0 || observed.Any(file => !file.IsReadable || file.SizeBytes <= 0 || file.Targets.Count == 0)
            || observed.Select(file => file.RemoteFileId).Distinct(StringComparer.Ordinal).Count() != observed.Count
            || observed.Select(file => file.LocalPath).Distinct(StringComparer.Ordinal).Count() != observed.Count
            || targets.Select(target => target.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != targets.Length)
            return Review("Every remote file must have unambiguous coverage and readable, size-matching local bytes.");
        if (selected.Count != targets.Length || selected.Select(item => item.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != targets.Length
            || selected.Select(item => item.EntityId).Distinct().Count() != selected.Count
            || selected.Select(item => item.SourceFileId).Distinct().Count() != selected.Count)
            return Review("Explicitly select every target once, including all episodes that share a file.");
        var bindings = new List<ManagedFileBinding>();
        foreach (var file in observed) {
            var owners = local.Where(owner => owner.LocalPath == file.LocalPath).ToArray();
            if (owners.Length != file.Targets.Count) return Review("The local source has different ownership coverage from the connected application.");
            var entities = new List<ManagedEntityBinding>();
            foreach (var target in file.Targets) {
                var selection = selected.SingleOrDefault(item => item.RemoteTargetId == target.RemoteTargetId);
                var matches = owners.Where(owner => selection is not null && owner.EntityId == selection.EntityId && owner.SourceFileId == selection.SourceFileId).ToArray();
                if (matches.Length != 1) return Review("A selected local source changed. Refresh the file matches before linking.");
                var owner = matches[0];
                if (owner.Kind != target.Kind || owner.SeasonNumber != target.SeasonNumber || owner.EpisodeNumber != target.EpisodeNumber
                    || target.AbsoluteNumber is not null && owner.AbsoluteNumber != target.AbsoluteNumber
                    || target.Kind == EntityKind.ComicInstallment &&
                       (string.IsNullOrWhiteSpace(target.IssueLabel) || owner.IssueLabel != target.IssueLabel))
                    return Review("The selected local entity's kind, episode numbering, or comic issue label differs from the remote target.");
                entities.Add(new(target, owner.EntityId, owner.SourceFileId));
            }
            bindings.Add(new(file.RemoteFileId, file.LocalPath, file.SizeBytes, file.WrittenAt, true, entities));
        }
        if (targets.Any(target => target.Kind is EntityKind.Book or EntityKind.AudioTrack)) {
            var allEbook = targets.All(target => target.Kind == EntityKind.Book) && targets.Length == 1;
            var allAudio = targets.All(target => target.Kind == EntityKind.AudioTrack);
            var parents = bindings.SelectMany(file => file.Entities)
                .Select(binding => local.Single(owner => owner.EntityId == binding.EntityId
                    && owner.SourceFileId == binding.SourceFileId).ParentEntityId)
                .Distinct().ToArray();
            if (!allEbook && (!allAudio || parents.Length != 1 || parents[0] is null))
                return Review("Select one book work and one rendition's exact source files.");
        }
        return new(bindings, null);
    }

    private static ManagedSourceAdoptionPlan Review(string reason) => new([], reason);
}
