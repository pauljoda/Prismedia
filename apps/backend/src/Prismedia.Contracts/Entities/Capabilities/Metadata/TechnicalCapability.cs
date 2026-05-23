namespace Prismedia.Contracts.Entities;

/// <summary>API-facing shared technical metadata capability.</summary>
[CapabilityKind("technical")]
public sealed record TechnicalCapability(
    TimeSpan? Duration,
    int? Width,
    int? Height,
    double? FrameRate,
    int? BitRate,
    int? SampleRate,
    int? Channels,
    string? Codec,
    string? Container,
    string? Format) : EntityCapability;
