using Prismedia.Application.Jobs.Handlers;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Read-only source and imported-file projection for acquisition management surfaces.</summary>
public sealed partial class AcquisitionService {
    /// <summary>The imported library files once imported, otherwise the in-progress download files.</summary>
    public async Task<AcquisitionFilesView> GetFilesAsync(Guid id, CancellationToken cancellationToken) {
        var info = await store.GetTransferInfoAsync(id, cancellationToken);
        if (info is null) {
            return new AcquisitionFilesView(false, []);
        }

        if (info.ImportResult is { } result) {
            return new AcquisitionFilesView(
                result.Phase == AcquisitionImportPhase.Imported,
                result.Files.Select(ToFileItem).ToArray(),
                result.Phase);
        }

        if (info.ImportResultUnavailable && string.IsNullOrWhiteSpace(info.FinalSourcePath)) {
            return new AcquisitionFilesView(
                false,
                [],
                info.Status == AcquisitionStatus.Importing ? AcquisitionImportPhase.Importing : null,
                true);
        }

        if (!string.IsNullOrWhiteSpace(info.FinalSourcePath)) {
            var imported = importedFiles.List(info.FinalSourcePath);
            return new AcquisitionFilesView(
                true,
                imported.Select(ToLegacyImportedFileItem).ToArray(),
                AcquisitionImportPhase.Imported,
                true);
        }

        if (info.ClientItemId is { } clientItemId) {
            var client = await ResolveClientAsync(info.DownloadClientConfigId, cancellationToken);
            if (client is not null) {
                var files = await clients.Get(client.Kind).GetFilesAsync(
                    ConnectionFor(client), clientItemId, cancellationToken);
                var phase = info.Status == AcquisitionStatus.Importing
                    ? AcquisitionImportPhase.Importing
                    : info.Status == AcquisitionStatus.Downloaded
                        ? AcquisitionImportPhase.Downloaded
                        : AcquisitionImportPhase.Downloading;
                return new AcquisitionFilesView(false, files.Select(ToFileItem).ToArray(), phase);
            }
        }

        return new AcquisitionFilesView(false, []);
    }

    private static AcquisitionFileItem ToFileItem(DownloadItemFile file) => new(
        file.Name,
        file.SizeBytes,
        file.Progress,
        SourceRelativePath: SafeDownloadRelativePath(file.Name),
        Role: AcquisitionImportFileRole.Unknown,
        ContentKind: AcquisitionImportFileLedger.ClassifyContentKind(file.Name),
        Status: file.Progress >= 1 ? AcquisitionImportFileStatus.Downloaded : null);

    private static AcquisitionFileItem ToLegacyImportedFileItem(DownloadItemFile file) => new(
        file.Name,
        file.SizeBytes,
        1,
        DestinationRelativePath: SafeDownloadRelativePath(file.Name),
        Role: AcquisitionImportFileRole.Unknown,
        ContentKind: AcquisitionImportFileLedger.ClassifyContentKind(file.Name),
        Status: AcquisitionImportFileStatus.Imported);

    private static AcquisitionFileItem ToFileItem(AcquisitionImportFileLedgerEntry file) => new(
        file.Name,
        file.SizeBytes,
        file.Status is AcquisitionImportFileStatus.Imported or AcquisitionImportFileStatus.Skipped ? 1 : 0,
        file.Id,
        file.SourceRelativePath,
        file.DestinationRelativePath,
        file.Role,
        file.ContentKind,
        file.Status,
        file.Decision,
        file.TechnicalError);

    private static string SafeDownloadRelativePath(string path) {
        if (Path.IsPathFullyQualified(path)) { return Path.GetFileName(path); }
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or "..")
            ? Path.GetFileName(normalized)
            : normalized;
    }
}
