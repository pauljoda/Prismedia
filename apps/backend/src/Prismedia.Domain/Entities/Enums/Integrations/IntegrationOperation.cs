namespace Prismedia.Domain.Entities;

/// <summary>Canonical operations in the versioned connected-application protocol.</summary>
public enum IntegrationOperation {
    /// <summary>Inspect remote identity, capabilities, and configuration choices.</summary>
    [Code("probe")]
    Probe,

    /// <summary>Search a source catalog.</summary>
    [Code("search")]
    Search,

    /// <summary>Browse a catalog page or container.</summary>
    [Code("browse")]
    Browse,

    /// <summary>Inspect a source URL.</summary>
    [Code("inspect")]
    Inspect,

    /// <summary>Resolve a source selection into an offer.</summary>
    [Code("resolve")]
    Resolve,

    /// <summary>Submit an idempotent transfer intent.</summary>
    [Code("submit")]
    Submit,

    /// <summary>Reconcile a submission by its durable operation key.</summary>
    [Code("find-submission")]
    FindSubmission,

    /// <summary>Atomically cancels an accepted operation or fences a late submission under that operation key.</summary>
    [Code("cancel-submission")]
    CancelSubmission,

    /// <summary>Read a durable remote job snapshot.</summary>
    [Code("get-job")]
    GetJob,

    /// <summary>Request cancellation of an owned remote job.</summary>
    [Code("cancel")]
    Cancel,

    /// <summary>Read the sealed manifest of exact output artifacts.</summary>
    [Code("list-artifacts")]
    ListArtifacts,

    /// <summary>Obtain bounded delivery authorization for one artifact.</summary>
    [Code("authorize-artifact")]
    AuthorizeArtifact,

    /// <summary>Renew guaranteed remote artifact retention while importing.</summary>
    [Code("renew-retention")]
    RenewRetention,

    /// <summary>Acknowledge committed local imports independently of transfer.</summary>
    [Code("acknowledge")]
    Acknowledge,

    /// <summary>Look up a remotely managed work.</summary>
    [Code("lookup-managed")]
    LookupManaged,

    /// <summary>Search a manager's upstream catalog without creating or changing a holding.</summary>
    [Code("discover-managed")]
    DiscoverManaged,

    /// <summary>Read external profile, root-folder, and request-policy choices without modifying them.</summary>
    [Code("manager-options")]
    ManagerOptions,

    /// <summary>Ensure a remote work exists using stable identities.</summary>
    [Code("ensure-managed")]
    EnsureManaged,

    /// <summary>Request acquisition of an explicit work and scope.</summary>
    [Code("request-managed")]
    RequestManaged,

    /// <summary>Change monitoring or profiles for an explicit scope.</summary>
    [Code("configure-managed")]
    ConfigureManaged,

    /// <summary>Read fulfillment and final file availability.</summary>
    [Code("reconcile-managed")]
    ReconcileManaged,

    /// <summary>Read exact monitoring and complete remote activity before releasing ownership.</summary>
    [Code("inspect-managed-release")]
    InspectManagedRelease,

    /// <summary>Search existing remote holdings.</summary>
    [Code("search-library")]
    SearchLibrary,

    /// <summary>Read exact remote holdings and file availability.</summary>
    [Code("get-library-item")]
    GetLibraryItem,

    /// <summary>List the external application's independently managed libraries.</summary>
    [Code("list-libraries")]
    ListLibraries,

    /// <summary>Idempotently request preparation of one exact source selection; never expands its scope.</summary>
    [Code("request-source")]
    RequestSource,

    /// <summary>Observe exact source preparation and file readiness without starting or changing remote work.</summary>
    [Code("observe-source")]
    ObserveSource,
}
