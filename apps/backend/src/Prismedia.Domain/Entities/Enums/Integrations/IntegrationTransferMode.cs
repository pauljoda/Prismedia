namespace Prismedia.Domain.Entities;

/// <summary>Who executes a selected acquisition; both paths require verified bytes and committed local ownership.</summary>
public enum IntegrationTransferMode {
    /// <summary>A connected executor owns the durable remote job and retained outputs.</summary>
    [Code("remote-executor")] RemoteExecutor,
    /// <summary>Prismedia retrieves a direct full-content offer from a connected source.</summary>
    [Code("source-download")] SourceDownload
}
