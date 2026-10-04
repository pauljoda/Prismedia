using Prismedia.Application.Files;

namespace Prismedia.Application.Acquisition;

public static partial class MusicImportPlanBuilder {
    #region Actions - Reviewed planning

    /// <summary>Retains exact reviewed track identities without guessing from filenames or importing unselected audio.</summary>
    public static ImportPlan PlanReviewed(IReadOnlyList<ImportCandidateFile> files,
        IReadOnlyList<ManualImportFileMapping> mappings, IReadOnlyList<RequestedAudioTrack> tracks,
        string artist, string album, string? template = null, int? year = null) {
        var targets = tracks.Select(track => track.EntityId).ToHashSet();
        var sources = new HashSet<string>(FileSystemPathComparison.Comparer);
        var selected = new List<ImportCandidateFile>();
        foreach (var mapping in mappings) {
            var file = files.SingleOrDefault(file =>
                FileSystemPathComparison.Equals(file.RelativePath, mapping.SourceRelativePath) && IsAudioFile(file.RelativePath));
            if (file is null || !targets.Remove(mapping.TargetEntityId) || !sources.Add(file.RelativePath)) {
                return ImportPlan.Block(ImportBlockReason.NoSupportedPayload);
            }
            selected.Add(file);
        }
        var plan = Plan(selected, artist, album, template, year);
        if (plan.Blocked) return plan;
        var targetBySource = mappings.ToDictionary(mapping => mapping.SourceRelativePath,
            mapping => mapping.TargetEntityId, FileSystemPathComparison.Comparer);
        return ImportPlan.For(plan.Items.Select(item => item with {
            TargetEntityId = targetBySource[item.SourceRelativePath]
        }).ToArray());
    }

    #endregion
}
