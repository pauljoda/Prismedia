using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

public sealed record AcquisitionImportFileLedgerEntry(
    string Id,
    string Name,
    long SizeBytes,
    string SourceRelativePath,
    string? DestinationRelativePath,
    AcquisitionImportFileRole Role,
    AcquisitionImportContentKind ContentKind,
    AcquisitionImportFileStatus Status,
    AcquisitionImportDecision Decision,
    string? TechnicalError);
