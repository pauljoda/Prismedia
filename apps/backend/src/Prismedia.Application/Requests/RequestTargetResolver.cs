using Prismedia.Application.Acquisition;
using Prismedia.Application.Security;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Resolves an interactive request's acquisition choices inside the current user's library grants.
/// Administrators and background workers retain the existing unrestricted targeting behavior.
/// </summary>
public sealed class RequestTargetResolver(
    ICurrentUserContext currentUser,
    SettingsService settings,
    BookAcquisitionProfileCommandService profiles) {
    /// <summary>
    /// Validates explicit member choices and supplies a visible compatible default when a client omits
    /// them. This prevents a member request from inheriting an administrator-only profile or library.
    /// </summary>
    public async Task<AcquisitionTargeting> ResolveAsync(
        RequestKindDescriptor descriptor,
        AcquisitionTargeting requested,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var allowedRootIds = await currentUser.GetAllowedLibraryRootIdsAsync(cancellationToken);
        if (allowedRootIds is null) {
            return requested;
        }

        var profileKind = descriptor.ProfileEntityKind
            ?? throw new RequestCommitValidationException("This request kind has no acquisition profile policy.");
        var profilePolicy = EntityKindRegistry.Describe(profileKind).AcquisitionProfile
            ?? throw new RequestCommitValidationException("This request kind has no acquisition profile policy.");
        var compatibleRoots = (await settings.ListLibraryRootsAsync(cancellationToken))
            .Where(root =>
                root.Enabled &&
                allowedRootIds.Contains(root.Id) &&
                (!hideNsfw || !root.IsNsfw) &&
                Supports(root, profilePolicy.LibraryRootMediaCapability))
            .OrderBy(root => root.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var availableProfiles = (await profiles.ListAsync(hideNsfw, cancellationToken))
            .Where(profile => profile.Kind == profileKind)
            .ToArray();
        var chosenProfile = requested.ProfileId is { } profileId
            ? availableProfiles.FirstOrDefault(profile => profile.Id == profileId)
                ?? throw new RequestCommitValidationException("The selected acquisition profile is not available to this user.")
            : availableProfiles.FirstOrDefault(profile => profile.IsDefault)
                ?? availableProfiles.FirstOrDefault();

        LibraryRoot? chosenRoot;
        if (requested.TargetLibraryRootId is { } rootId) {
            chosenRoot = compatibleRoots.FirstOrDefault(root => root.Id == rootId)
                ?? throw new RequestCommitValidationException("The selected library is not available for this request.");
        } else {
            chosenRoot = compatibleRoots.FirstOrDefault(root => root.Id == chosenProfile?.TargetLibraryRootId)
                ?? compatibleRoots.FirstOrDefault();
        }

        if (chosenRoot is null) {
            throw new RequestCommitValidationException("No accessible library supports this kind of content.");
        }

        return new AcquisitionTargeting(chosenRoot.Id, chosenProfile?.Id);
    }

    private static bool Supports(LibraryRoot root, LibraryRootMediaCapability capability) =>
        capability switch {
            LibraryRootMediaCapability.ScanBooks => root.ScanBooks,
            LibraryRootMediaCapability.ScanVideos => root.ScanVideos,
            LibraryRootMediaCapability.ScanAudio => root.ScanAudio,
            _ => false
        };
}
