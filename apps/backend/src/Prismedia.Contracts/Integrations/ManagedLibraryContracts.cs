using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Searches existing connected holdings, independently of metadata discovery and acquisition.</summary>
public sealed record ManagedLibraryQuery(EntityKind EntityKind, string? Query = null, string? Cursor = null, int Limit = 25);
/// <summary>An external application's current holding. File counts are remote claims, not local playback availability.</summary>
public sealed record ManagedLibraryItem(string RemoteId, EntityKind EntityKind, string Title, int? Year,
    IReadOnlyDictionary<string, string> ExternalIds, bool Monitored, string? ProfileId, int? RemoteFileCount,
    ManagedLibraryPresentation? Presentation = null);
/// <summary>Optional source-owned presentation metadata for a connected holding; it is never persisted as Prismedia library metadata.</summary>
/// <param name="Overview">Source description shown in the read-only holding detail.</param>
/// <param name="PosterUrl">Absolute HTTP(S) poster image URL, when the source exposes one.</param>
/// <param name="BackdropUrl">Absolute HTTP(S) backdrop image URL, when the source exposes one.</param>
/// <param name="Genres">Source genre labels shown as read-only context.</param>
/// <param name="RuntimeMinutes">Source runtime in whole minutes.</param>
/// <param name="ContentRating">Source certification or content rating.</param>
public sealed record ManagedLibraryPresentation(
    string? Overview = null,
    string? PosterUrl = null,
    string? BackdropUrl = null,
    IReadOnlyList<string>? Genres = null,
    int? RuntimeMinutes = null,
    string? ContentRating = null);
/// <summary>Bounded observed holdings with adapter-owned pagination.</summary>
public sealed record ManagedLibraryPage(IReadOnlyList<ManagedLibraryItem> Items, string? NextCursor = null);
/// <summary>A provider-owned library that can be attached to one ordinary Prismedia library root.</summary>
public sealed record ProviderLibraryDescriptor(
    string RemoteId,
    string Label,
    string RemotePath,
    IReadOnlyList<EntityKind> EntityKinds,
    string? ManagementUrl = null);
/// <summary>The complete bounded set of libraries exposed by one connected application.</summary>
public sealed record ProviderLibraryCatalog(IReadOnlyList<ProviderLibraryDescriptor> Libraries);
/// <summary>A connected application's provider libraries and any isolated discovery failure.</summary>
public sealed record ProviderLibraryConnection(
    Guid ConnectionId,
    string ConnectionName,
    string PluginId,
    IReadOnlyList<ProviderLibraryDescriptor> Libraries,
    string? Error = null);
/// <summary>Addresses a holding and pins known external identities to detect a reused remote numeric ID.</summary>
public sealed record ManagedItemInput(EntityKind EntityKind, string RemoteId, IReadOnlyDictionary<string, string> ExpectedExternalIds);
/// <summary>One content target covered by an externally managed file. IssueLabel preserves comic designations such as ½ and 12.5 independently of television numbering.</summary>
public sealed record ManagedFileTarget(string RemoteId, EntityKind EntityKind, string Title,
    int? SeasonNumber = null, int? EpisodeNumber = null, int? AbsoluteNumber = null, string? IssueLabel = null);
/// <summary>Remote file evidence. Paths belong to the connected server and require an explicit local mapping before access.</summary>
public sealed record ManagedLibraryFile(string RemoteId, string Path, long SizeBytes, DateTimeOffset? AddedAt,
    IReadOnlyList<ManagedFileTarget> Targets);
/// <summary>Current remote item and exact file associations; an empty file list is not inferred from a completed command.</summary>
public sealed record ManagedItemSnapshot(ManagedLibraryItem Item, string Path, IReadOnlyList<ManagedLibraryFile> Files, DateTimeOffset ObservedAt);
/// <summary>Requests the connected application's own profile and root choices for this kind.</summary>
public sealed record ManagerOptionsInput(EntityKind EntityKind);
/// <summary>An opaque external profile or policy ID and its display name.</summary>
public sealed record ManagerChoice(string Id, string Label);
/// <summary>An existing external root; its path is not a Prismedia destination.</summary>
public sealed record ManagerRootChoice(string Id, string Path, bool? Accessible);
/// <summary>External choices remain external IDs; the host does not translate a manager's quality system into its own.</summary>
public sealed record ManagerOptions(IReadOnlyList<ManagerChoice> Profiles, IReadOnlyList<ManagerRootChoice> Roots);
