using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prismedia.Contracts.Integrations;
using Prismedia.Application.Requests;

namespace Prismedia.Application.Integrations;

/// <summary>Stable replay identity for the complete reviewed work, its scopes, and the external fulfillment decision.</summary>
internal static class ReviewedManagedRequestIdentity {
    #region Actions - Fingerprints

    public static string Fingerprint(Guid connectionId, CommitReviewedManagedRequestInput input) {
        var request = input.Request;
        var value = new {
            ConnectionId = connectionId,
            input.ProfileId,
            input.Monitored,
            input.Search,
            input.ManagerDiscoveryRevision,
            input.EntityId,
            TargetEntityIds = input.TargetEntityIds?.Order().ToArray(),
            Scopes = input.Scopes
                .Select(scope => new { scope.Rendition, scope.LibraryRootId })
                .OrderBy(scope => scope.Rendition)
                .ToArray(),
            Connected = input.Connected is null ? null : new {
                input.Connected.Item.EntityKind,
                input.Connected.Item.RemoteId,
                input.Connected.Item.BookRendition,
                ExpectedExternalIds = input.Connected.Item.ExpectedExternalIds.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
                input.Connected.RemoteTargetId,
                input.Connected.TargetLabel
            },
            Request = request is null ? null : new {
                request.Kind,
                request.PluginId,
                request.RootExternalIdentity,
                request.ProposalRevision,
                SelectedProposalIds = request.SelectedProposalIds.Order(StringComparer.Ordinal).ToArray(),
                SelectedFields = request.SelectedFields?.Order(StringComparer.Ordinal).ToArray(),
                SelectedImages = request.SelectedImages?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
                SelectedProposalRevision = request.Proposal is null ? null : RequestProposalRevision.Compute(request.Proposal)
            }
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    }

    #endregion
}
