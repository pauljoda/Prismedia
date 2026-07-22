namespace Prismedia.Domain.Entities;

public enum AcquisitionImportPhase {
    [Code("downloading")] Downloading,
    [Code("downloaded")] Downloaded,
    [Code("importing")] Importing,
    [Code("imported")] Imported,
}
