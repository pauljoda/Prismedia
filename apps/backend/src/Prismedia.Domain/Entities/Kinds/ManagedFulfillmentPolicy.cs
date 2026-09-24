namespace Prismedia.Domain.Entities;

/// <summary>
/// Immutable managed-fulfillment rules owned by one Entity kind: whether requests use a manager quality
/// profile, which local targets a request names, what monitoring and search it must ask for, whether the
/// manager may create the remote holding, and which provider identities pin the work.
/// </summary>
public sealed record ManagedFulfillmentPolicy {
    #region Static Variables

    private const int UnboundedTargets = 10_000;

    #endregion

    #region Variables

    /// <summary>Whether requests and controls select one of the manager's quality profiles.</summary>
    public bool UsesProfile { get; }

    /// <summary>Targets a holding contains, when the kind has no independent renditions.</summary>
    public ManagedTarget? Target { get; }

    /// <summary>Targets for each independently fulfilled rendition; empty when the kind has none.</summary>
    public IReadOnlyDictionary<BookRendition, ManagedTarget> RenditionTargets { get; }

    /// <summary>Fewest explicit local targets a request names.</summary>
    public int MinimumTargets { get; }

    /// <summary>Most explicit local targets a request names.</summary>
    public int MaximumTargets { get; }

    /// <summary>Monitoring a request must ask for, or null when either choice is allowed.</summary>
    public bool? RequiredMonitoring { get; }

    /// <summary>Whether a request must ask the manager to search.</summary>
    public bool RequiresSearch { get; }

    /// <summary>Whether a request may ask the manager to create its remote holding; otherwise it must already exist.</summary>
    public bool CreatesHolding { get; }

    /// <summary>Whether monitoring applies to the whole remote item rather than to each target.</summary>
    public bool MonitorsWholeItem { get; }

    /// <summary>Whether a manager action is scoped to one exact selected target.</summary>
    public bool SelectsControlTarget { get; }

    /// <summary>Provider identity namespaces that pin the requested work at the manager.</summary>
    public IReadOnlyList<string> IdentityProviders { get; }

    /// <summary>How the pinning identity is described to a person, completing "Identify this work with …".</summary>
    public string IdentityDescription { get; }

    /// <summary>Provider identity namespaces that pin each explicit target, empty when targets need none.</summary>
    public IReadOnlyList<string> TargetIdentityProviders { get; }

    /// <summary>Whether each request must choose one independently fulfilled rendition.</summary>
    public bool RequiresRendition => RenditionTargets.Count > 0;

    /// <summary>Whether a holding gains further targets through later reviewed expansion rather than a second request.</summary>
    public bool AccumulatesTargets => MaximumTargets > 1;

    /// <summary>Whether the reviewed monitoring choice is applied; otherwise the manager's target monitoring is preserved.</summary>
    public bool AppliesReviewedMonitoring => RequiredMonitoring != false;

    #endregion

    #region Constructors

    /// <summary>Creates one validated managed-fulfillment policy.</summary>
    /// <exception cref="ArgumentException">The rules contradict each other.</exception>
    public ManagedFulfillmentPolicy(
        IReadOnlyList<string> identityProviders,
        string identityDescription,
        bool usesProfile,
        ManagedTarget? target = null,
        IReadOnlyDictionary<BookRendition, ManagedTarget>? renditionTargets = null,
        int minimumTargets = 0,
        int maximumTargets = 0,
        bool? requiredMonitoring = null,
        bool requiresSearch = false,
        bool createsHolding = true,
        bool monitorsWholeItem = false,
        bool selectsControlTarget = false,
        IReadOnlyList<string>? targetIdentityProviders = null) {
        RenditionTargets = renditionTargets ?? new Dictionary<BookRendition, ManagedTarget>();
        if (identityProviders is not { Count: > 0 }) {
            throw new ArgumentException("A managed kind needs at least one pinning identity provider.", nameof(identityProviders));
        }

        if (target is null == (RenditionTargets.Count == 0)) {
            throw new ArgumentException("Declare either one target or a target per rendition.", nameof(target));
        }

        if (minimumTargets < 0 || maximumTargets < minimumTargets || maximumTargets > UnboundedTargets) {
            throw new ArgumentException("The explicit target range is invalid.", nameof(maximumTargets));
        }

        if (selectsControlTarget && maximumTargets != 1) {
            throw new ArgumentException("An exact control target requires exactly one explicit request target.", nameof(selectsControlTarget));
        }

        IdentityProviders = identityProviders;
        IdentityDescription = string.IsNullOrWhiteSpace(identityDescription)
            ? throw new ArgumentException("Describe the identity a manager needs.", nameof(identityDescription))
            : identityDescription;
        TargetIdentityProviders = targetIdentityProviders ?? [];
        UsesProfile = usesProfile;
        Target = target;
        MinimumTargets = minimumTargets;
        MaximumTargets = maximumTargets;
        RequiredMonitoring = requiredMonitoring;
        RequiresSearch = requiresSearch;
        CreatesHolding = createsHolding;
        MonitorsWholeItem = monitorsWholeItem;
        SelectsControlTarget = selectsControlTarget;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Whether a connected manager can fulfill <paramref name="kind"/>.</summary>
    public static bool Supports(EntityKind kind) => EntityKindRegistry.Describe(kind) is IManagedFulfillmentKindDefinition;

    /// <summary>Returns the managed-fulfillment rules declared by <paramref name="kind"/>'s definition.</summary>
    /// <exception cref="ArgumentException">The kind cannot be fulfilled by a connected manager.</exception>
    public static ManagedFulfillmentPolicy For(EntityKind kind) {
        var definition = EntityKindRegistry.Describe(kind);
        return definition is IManagedFulfillmentKindDefinition managed
            ? managed.ManagedFulfillment
            : throw new ArgumentException($"A connected manager cannot fulfill {definition.GroupLabel.ToLowerInvariant()}.", nameof(kind));
    }

    #endregion

    #region Actions - Targets

    /// <summary>A finite child scope accepts any number of explicit targets up to the bound.</summary>
    public static int FiniteChildren => UnboundedTargets;

    /// <summary>Returns the targets a holding contains, requiring a rendition exactly when the kind has renditions.</summary>
    /// <exception cref="ArgumentException">The rendition is missing, unexpected, or not supported.</exception>
    public ManagedTarget TargetFor(BookRendition? rendition) {
        if (!RequiresRendition) {
            return rendition is null
                ? Target!
                : throw new ArgumentException("Only works with independent renditions accept a rendition.", nameof(rendition));
        }

        return rendition is { } chosen && RenditionTargets.TryGetValue(chosen, out var target)
            ? target
            : throw new ArgumentException("Choose one exact rendition for this work.", nameof(rendition));
    }

    /// <summary>Whether a rendition choice is valid for this kind: required exactly when the kind has renditions.</summary>
    public bool AcceptsRendition(BookRendition? rendition) =>
        RequiresRendition ? rendition is { } chosen && RenditionTargets.ContainsKey(chosen) : rendition is null;

    #endregion

    #region Actions - Requests

    /// <summary>Validates a reviewed request against this kind's rules, naming the first rule it breaks.</summary>
    /// <exception cref="ArgumentException">The request breaks one of this kind's managed-fulfillment rules.</exception>
    public void RequireRequest(string? profileId, BookRendition? rendition, int targetCount, IReadOnlyList<string?> targetLabels,
        bool monitored, bool search) {
        var target = TargetFor(rendition);
        if (UsesProfile && string.IsNullOrWhiteSpace(profileId)) {
            throw new ArgumentException("Choose the manager profile for this request.", nameof(profileId));
        }

        if (!UsesProfile && profileId is not null) {
            throw new ArgumentException("This kind is requested without a manager profile.", nameof(profileId));
        }

        if (targetCount < MinimumTargets || targetCount > MaximumTargets || targetLabels.Count != targetCount) {
            throw new ArgumentException(MaximumTargets == 0
                ? "This work is requested as a whole, without separate targets."
                : $"Select between {MinimumTargets} and {MaximumTargets} reviewed targets.", nameof(targetCount));
        }

        if (target.Shape.RequiresIssueLabel && targetLabels.Any(string.IsNullOrWhiteSpace)) {
            throw new ArgumentException("Each selected issue needs its exact issue number.", nameof(targetLabels));
        }

        if (RequiredMonitoring is { } required && monitored != required) {
            throw new ArgumentException(required
                ? "This work must be monitored by its manager."
                : "Selected targets are requested without turning on broad monitoring.", nameof(monitored));
        }

        if (RequiresSearch && !search) {
            throw new ArgumentException("This request must ask its manager to search for the selected targets.", nameof(search));
        }
    }

    #endregion
}
