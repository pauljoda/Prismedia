using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Application.Settings;

namespace Prismedia.Application.Integrations;

/// <summary>Configures explicit remote-to-local library boundaries and separates remote availability from local byte access.</summary>
public sealed class ExternalLibraryService(
    IExternalLibraryMountStore mounts,
    ManagedLibraryService managedLibrary,
    ProviderLibraryService providerLibraries,
    IntegrationConnectionAccess access,
    SettingsService settings) {
    /// <summary>Reads retained mappings without requiring the remote application to be online.</summary>
    public Task<IReadOnlyList<ExternalLibraryMount>> ListAsync(Guid connectionId, CancellationToken token) => mounts.ListAsync(connectionId, token);

    /// <summary>Lists enabled mappings whose current provider descriptor and local scan configuration support a kind.</summary>
    public async Task<IReadOnlyList<ExternalLibraryMount>> ListSuitableAsync(Guid connectionId, EntityKind kind, CancellationToken token) {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Choose a supported library kind.");
        var configured = await mounts.ListAsync(connectionId, token);
        var roots = (await settings.ListLibraryRootsAsync(token)).ToDictionary(root => root.Id);
        var discovered = await providerLibraries.ListAsync(connectionId, token);
        var available = discovered.Libraries
            .Where(library => library.EntityKinds.Contains(kind))
            .Select(library => (library.RemoteId, library.RemotePath))
            .ToHashSet();
        return configured.Where(mount => available.Contains((mount.RemoteRootId, mount.RemotePath))
            && roots.TryGetValue(mount.LibraryRootId, out var root) && root.Enabled && Supports(root, kind)).ToArray();
    }

    /// <summary>Verifies current root choices before creating a paused, read-only local library.</summary>
    public async Task<ExternalLibraryMount> CreateAsync(Guid connectionId, CreateExternalLibraryMountRequest request, CancellationToken token) {
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Length > 512 || request.Label.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(request.RemoteRootId) || request.RemoteRootId.Length > 512)
            throw new ArgumentException("Choose an external root and a library label up to 512 characters.");
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ConnectedLibrary, IntegrationOperation.ListLibraries, token);
        await RequireProviderLibraryAsync(connectionId, request.RemoteRootId, request.ExpectedRemotePath, request.EntityKind, token);
        return await mounts.CreateAsync(connectionId, authorized.Connection.State.Revision, request, token);
    }

    /// <summary>Verifies current root choices before attaching an existing local library as an immutable external boundary.</summary>
    public async Task<ExternalLibraryMount> AttachAsync(Guid connectionId, AttachExistingExternalLibraryMountRequest request, CancellationToken token) {
        if (string.IsNullOrWhiteSpace(request.RemoteRootId) || request.RemoteRootId.Length > 512)
            throw new ArgumentException("Choose an external root.");
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ConnectedLibrary, IntegrationOperation.ListLibraries, token);
        await RequireProviderLibraryAsync(connectionId, request.RemoteRootId, request.ExpectedRemotePath, request.EntityKind, token);
        var attachment = await mounts.AttachWithResultAsync(connectionId, authorized.Connection.State.Revision, request, token);
        if (attachment.Created) {
            await settings.QueueLibraryRootScansIfEnabledAsync(
                attachment.Mount.LibraryRootId,
                "attaching external library",
                token);
        }
        return attachment.Mount;
    }

    /// <summary>Reads a fresh holding, checks its identity, then inspects only the files it currently reports.</summary>
    public async Task<MappedLibrarySnapshot> InspectAsync(Guid connectionId, ManagedItemInput request, CancellationToken token) {
        var remote = await managedLibrary.GetAsync(connectionId, request, token);
        return new(remote, await mounts.InspectAsync(connectionId, remote.Files, token));
    }

    private async Task RequireProviderLibraryAsync(Guid connectionId, string remoteId, string expectedPath,
        EntityKind entityKind, CancellationToken token) {
        var catalog = await providerLibraries.ListAsync(connectionId, token);
        if (!catalog.Libraries.Any(library => library.RemoteId == remoteId
            && library.RemotePath == expectedPath && library.EntityKinds.Contains(entityKind)))
            throw new ArgumentException("The external library changed. Refresh its choices before mapping it.");
    }

    private static bool Supports(Prismedia.Contracts.Settings.LibraryRoot root, EntityKind kind) =>
        EntityKindRegistry.Describe(kind).LibraryRootCapability is { } capability && root.Scans(capability);
}
