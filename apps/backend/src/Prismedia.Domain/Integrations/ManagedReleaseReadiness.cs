namespace Prismedia.Domain.Integrations;

/// <summary>Acquisition ownership remains reserved until monitoring and all observed activity have stopped.</summary>
public sealed record ManagedReleaseReadiness(bool AnyTargetMonitored, bool QueueEmpty, bool CommandsIdle) {
    /// <summary>The blocking fact to present to the user, or null when the observed scope may be released.</summary>
    public string? Problem => AnyTargetMonitored ? "Turn off monitoring for every selected target before releasing this owner."
        : !QueueEmpty ? "The connected application still has download activity. Wait for it to settle before releasing ownership."
        : !CommandsIdle ? "The connected application has running or unverified commands. Ownership remains reserved."
        : null;
}
