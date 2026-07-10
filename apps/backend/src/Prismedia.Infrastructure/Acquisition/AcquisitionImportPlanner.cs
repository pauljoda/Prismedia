using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Resolves a completed download into concrete file moves. Enumerates the supported book files under the
/// download content path, delegates payload selection and target-path rendering to the pure
/// <see cref="ImportPlanBuilder"/>, then resolves the results to absolute paths and guards against any
/// target escaping the library root.
/// </summary>
public sealed class AcquisitionImportPlanner : IAcquisitionImportPlanner {
    public Task<ResolvedImportPlan> PlanAsync(
        string contentPath,
        string libraryRootPath,
        BookImportProfile profile,
        ImportTemplateContext context,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(contentPath) || (!File.Exists(contentPath) && !Directory.Exists(contentPath))) {
            return Task.FromResult(ResolvedImportPlan.Block(ImportBlockReason.NoSupportedPayload));
        }

        // Enumerate candidate files relative to the content root (a single file content path is itself the root).
        string contentRoot;
        IReadOnlyList<string> relativeFiles;
        if (File.Exists(contentPath)) {
            contentRoot = Path.GetDirectoryName(contentPath) ?? contentPath;
            relativeFiles = [Path.GetFileName(contentPath)];
        } else {
            contentRoot = contentPath;
            relativeFiles = Directory
                .EnumerateFiles(contentPath, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(contentPath, file))
                .ToArray();
        }

        var plan = ImportPlanBuilder.Plan(relativeFiles, context, profile.PathTemplate);
        if (plan.Blocked) {
            return Task.FromResult(new ResolvedImportPlan(true, plan.BlockReason, []));
        }

        var rootFull = Path.GetFullPath(libraryRootPath);
        var items = new List<ResolvedImportItem>(plan.Items.Count);
        foreach (var item in plan.Items) {
            var sourceAbsolute = Path.GetFullPath(Path.Combine(contentRoot, item.SourceRelativePath));
            var targetAbsolute = Path.GetFullPath(Path.Combine(rootFull, item.TargetRelativePath));

            // Refuse any target that resolves outside the library root (path-traversal guard).
            if (!IsUnderRoot(targetAbsolute, rootFull)) {
                return Task.FromResult(ResolvedImportPlan.Block(ImportBlockReason.NoSupportedPayload));
            }

            items.Add(new ResolvedImportItem(sourceAbsolute, targetAbsolute));
        }

        return Task.FromResult(new ResolvedImportPlan(false, null, items));
    }

    private static bool IsUnderRoot(string candidate, string root) {
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, FileSystemPathComparison.Comparison);
    }
}
