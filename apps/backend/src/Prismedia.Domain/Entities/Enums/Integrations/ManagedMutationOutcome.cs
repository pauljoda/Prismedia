namespace Prismedia.Domain.Entities;

/// <summary>Definite outcomes of one manager mutation. Transport failures provide no such guarantee.</summary>
public enum ManagedMutationOutcome {
    /// <summary>The explicitly requested configuration was observed after applying or was already satisfied.</summary>
    [Code("applied")] Applied,
    /// <summary>A command was accepted with an exact remote identifier and matching scope.</summary>
    [Code("accepted")] Accepted,
    /// <summary>The adapter establishes that this invocation did not apply its requested mutation.</summary>
    [Code("rejected")] Rejected
}
