namespace Prismedia.Application.Integrations;

/// <summary>A requested scope overlaps an existing acquisition owner and requires an explicit handoff.</summary>
/// <param name="innerException">The database or store failure that detected the overlap, when there was one.</param>
/// <param name="problem">A specific explanation of which owner holds the scope; a general one is used otherwise.</param>
public sealed class FulfillmentOwnershipConflictException(Exception? innerException = null, string? problem = null)
    : ArgumentException(problem ?? GeneralProblem, innerException) {
    #region Static Variables

    private const string GeneralProblem =
        "This work and scope already has an acquisition owner. Use its connection or resolve the existing ownership first.";

    #endregion
}
