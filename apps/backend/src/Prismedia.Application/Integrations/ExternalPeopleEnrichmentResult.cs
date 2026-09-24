namespace Prismedia.Application.Integrations;

/// <summary>Outcome of one exact external-library people enrichment.</summary>
/// <param name="Applied">Whether credits were applied.</param>
/// <param name="Provider">Provider that supplied credits, when applied.</param>
/// <param name="Message">Concise job progress message.</param>
public sealed record ExternalPeopleEnrichmentResult(
    bool Applied,
    string? Provider,
    string Message);
