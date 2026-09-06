using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Checks recovered video bytes before a placed import can publish episode availability.</summary>
internal static class TvPlacedFileValidation {
    /// <summary>Decodes each physical file once, retaining its location in any review reason.</summary>
    public static async Task<string?> ValidateAsync(JobContext context, IEnumerable<string> paths,
        IVideoPayloadVerifier verifier, CancellationToken cancellationToken) {
        var files = paths.Distinct(FileSystemPathComparison.Comparer).ToArray();
        for (var index = 0; index < files.Length; index++) {
            await context.ReportProgressAsync(30, $"Verifying recovered episode file {index + 1} of {files.Length}", cancellationToken);
            if (await verifier.FindFailureAsync(files[index], cancellationToken) is { } failure) {
                return $"{Path.GetFileName(files[index])}: {failure}";
            }
        }
        return null;
    }
}
