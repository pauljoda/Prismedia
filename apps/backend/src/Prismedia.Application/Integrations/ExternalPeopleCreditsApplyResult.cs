namespace Prismedia.Application.Integrations;

/// <summary>Outcome of the atomic missing-credits apply.</summary>
public enum ExternalPeopleCreditsApplyResult {
    /// <summary>The provider credits were applied.</summary>
    Applied,

    /// <summary>Credits already existed when the lifecycle lease was acquired.</summary>
    ExistingCredits,

    /// <summary>A user-authored credits edit or clear protects the section from enrichment.</summary>
    ProtectedByUser,

    /// <summary>The target no longer exists.</summary>
    NotFound,

    /// <summary>Destructive lifecycle ownership prevented the mutation.</summary>
    LifecycleConflict
}
