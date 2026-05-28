using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Implements entity persistence operations for library scanning against the entity schema.
/// </summary>
public sealed partial class LibraryScanPersistenceService(PrismediaDbContext db) :
    ILibraryScanRootPersistence,
    IVideoScanPersistence,
    IImageGalleryScanPersistence,
    IAudioScanPersistence,
    IBookScanPersistence,
    IDownstreamNeedsPersistence,
    IMediaProcessingStatePersistence,
    IEntityRefreshTreePersistence {
    private readonly PrismediaDbContext _db = db;
}
