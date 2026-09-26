namespace Prismedia.Domain.Entities;

/// <summary>
/// Opt-in facet for kinds a connected source or executor can import as exact files. Import services ask the
/// kind's definition which formats, byte limits, and library roots it accepts instead of naming kinds.
/// </summary>
public interface IIntegrationImportKindDefinition {
    /// <summary>Formats, limits, and destination rules for imports of this kind.</summary>
    IntegrationImportPolicy IntegrationImport { get; }
}
