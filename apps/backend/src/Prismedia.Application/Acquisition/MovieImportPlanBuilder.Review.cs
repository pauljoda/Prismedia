using Prismedia.Application.Files;

namespace Prismedia.Application.Acquisition;

public static partial class MovieImportPlanBuilder {
    #region Actions - Reviewed planning

    /// <summary>Plans only the exact video the user assigned to this movie, rechecking the current payload.</summary>
    public static ImportPlan PlanReviewed(IReadOnlyList<ImportCandidateFile> files,
        IReadOnlyList<ManualImportFileMapping> mappings, Guid? entityId, ImportTemplateContext context,
        string? template = null, string? quality = null) {
        if (mappings.Count != 1 || entityId is null || mappings[0].TargetEntityId != entityId) {
            return ImportPlan.Block(ImportBlockReason.AmbiguousMultiplePrimaries);
        }
        var file = files.SingleOrDefault(file =>
            FileSystemPathComparison.Equals(file.RelativePath, mappings[0].SourceRelativePath)
            && VideoExtensions.Contains(Path.GetExtension(file.RelativePath)));
        return file is null ? ImportPlan.Block(ImportBlockReason.NoSupportedPayload)
            : Plan([file], context, template, quality);
    }

    #endregion
}
