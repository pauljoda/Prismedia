using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Protects source cursors and selections from tampering and cross-connection reuse.</summary>
public interface IDiscoveryTokenProtector {
    #region Abstract Methods

    /// <summary>Issues an expiring item-selection token scoped to the connection.</summary>
    string ProtectSelection(Guid connectionId, SourceSelection selection);

    /// <summary>Reads an unexpired selection, or reports an invalid selection without exposing payload content.</summary>
    SourceSelection ReadSelection(Guid connectionId, string token);

    /// <summary>Issues an expiring continuation token bound to the connection, kind, query, and container.</summary>
    string ProtectCursor(Guid connectionId, BrowseConnectionRequest scope, string cursor);

    /// <summary>Reads a cursor only in the exact discovery context that produced it.</summary>
    string ReadCursor(Guid connectionId, BrowseConnectionRequest scope, string token);

    #endregion
}
