using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Configures explicit remote-to-local library boundaries and separates remote availability from local byte access.</summary>
public sealed class ExternalLibraryService(IExternalLibraryMountStore mounts, ManagedLibraryService library, IntegrationConnectionAccess access) {
    /// <summary>Reads retained mappings without requiring the remote application to be online.</summary>
    public Task<IReadOnlyList<ExternalLibraryMount>> ListAsync(Guid connectionId, CancellationToken token) => mounts.ListAsync(connectionId, token);

    /// <summary>Verifies current root choices before creating a paused, read-only local library.</summary>
    public async Task<ExternalLibraryMount> CreateAsync(Guid connectionId, CreateExternalLibraryMountRequest request, CancellationToken token) {
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Length > 512 || request.Label.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(request.RemoteRootId) || request.RemoteRootId.Length > 512)
            throw new ArgumentException("Choose an external root and a library label up to 512 characters.");
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.ManagerOptions, request.EntityKind, token);
        var choices = await library.OptionsAsync(connectionId, new(request.EntityKind), token);
        if (!choices.Roots.Any(root => root.Id == request.RemoteRootId && root.Path == request.ExpectedRemotePath))
            throw new ArgumentException("The external root changed. Refresh its choices before mapping it.");
        return await mounts.CreateAsync(connectionId, authorized.Connection.State.Revision, request, token);
    }

    /// <summary>Verifies current root choices before attaching an existing local library as an immutable external boundary.</summary>
    public async Task<ExternalLibraryMount> AttachAsync(Guid connectionId, AttachExistingExternalLibraryMountRequest request, CancellationToken token) {
        if (string.IsNullOrWhiteSpace(request.RemoteRootId) || request.RemoteRootId.Length > 512)
            throw new ArgumentException("Choose an external root.");
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.ManagerOptions, request.EntityKind, token);
        var choices = await library.OptionsAsync(connectionId, new(request.EntityKind), token);
        if (!choices.Roots.Any(root => root.Id == request.RemoteRootId && root.Path == request.ExpectedRemotePath))
            throw new ArgumentException("The external root changed. Refresh its choices before mapping it.");
        return await mounts.AttachAsync(connectionId, authorized.Connection.State.Revision, request, token);
    }

    /// <summary>Reads a fresh holding, checks its identity, then inspects only the files it currently reports.</summary>
    public async Task<MappedLibrarySnapshot> InspectAsync(Guid connectionId, ManagedItemInput request, CancellationToken token) {
        var remote = await library.GetAsync(connectionId, request, token);
        return new(remote, await mounts.InspectAsync(connectionId, remote.Files, token));
    }
}
