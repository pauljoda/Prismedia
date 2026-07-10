using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Implements entity persistence operations for library scanning against the entity schema.
/// </summary>
public sealed partial class LibraryScanPersistenceService(
    PrismediaDbContext db,
    AssetPathService? assets = null,
    IEntityLifecycleMutationLease? lifecycle = null) :
    ILibraryScanRootPersistence,
    IVideoScanPersistence,
    IImageGalleryScanPersistence,
    IAudioScanPersistence,
    IBookScanPersistence,
    IDownstreamNeedsPersistence,
    IMediaProcessingStatePersistence,
    IEntityRefreshTreePersistence,
    IScanMetadataPersistence {
    private readonly PrismediaDbContext _db = db;
    private readonly AssetPathService? _assets = assets;
    private readonly IEntityLifecycleMutationLease _lifecycle =
        lifecycle ?? new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db));
}
