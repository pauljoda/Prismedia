namespace Prismedia.Contracts.Entities;

/// <summary>Serialized-comic facts specific to one independently released installment.</summary>
/// <param name="InstallmentKind">Whether the release is a chapter, issue, special, or one-shot.</param>
[CapabilityKind("comic-installment-metadata")]
public sealed record ComicInstallmentMetadataCapability(
    Prismedia.Domain.Entities.ComicInstallmentKind InstallmentKind) : EntityCapability;
