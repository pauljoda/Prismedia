using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Host-owned validation of manager observations before they influence a saved action.</summary>
public static class ManagedControlValidation {
    #region Actions - Validation

    /// <summary>Requires exact pinned work identity, complete finite target coverage, and usable configuration.</summary>
    public static void Validate(ManagedControlScope scope, ManagedControlState state) {
        if (state?.Item is null || state.Capabilities is null || state.Item.EntityKind != scope.Item.EntityKind
            || state.Item.RemoteId != scope.Item.RemoteId
            || state.Item.ExternalIds is null
            || scope.Item.ExpectedExternalIds.Any(pair => !state.Item.ExternalIds.TryGetValue(pair.Key, out var value)
                || value != pair.Value)
            || string.IsNullOrWhiteSpace(state.Path) || state.Path.Length > 8192
            || ManagedFulfillmentPolicy.For(scope.Item.EntityKind).UsesProfile
                && string.IsNullOrWhiteSpace(state.Item.ProfileId)
            || state.Targets is null || state.Targets.Count != scope.Targets.Count || state.Targets.Any(target => target?.Target is null)
            || !scope.Targets.OrderBy(target => target.RemoteId, StringComparer.Ordinal)
                .SequenceEqual(state.Targets.Select(target => target.Target).OrderBy(target => target.RemoteId, StringComparer.Ordinal))) {
            throw new IntegrationInvocationException(
                "The manager returned a changed identity or incomplete configuration for the reviewed scope.");
        }
    }

    #endregion
}
