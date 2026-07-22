namespace Prismedia.Domain.Entities;

public enum AcquisitionImportFileStatus {
    [Code("downloaded")] Downloaded,
    [Code("pending-import")] PendingImport,
    [Code("importing")] Importing,
    [Code("imported")] Imported,
    [Code("skipped")] Skipped,
    [Code("failed")] Failed,
}
