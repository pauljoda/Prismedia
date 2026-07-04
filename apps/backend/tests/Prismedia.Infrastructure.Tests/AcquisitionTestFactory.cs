using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Shared construction helpers for the acquisition stores under test. <see cref="EfAcquisitionStore"/>
/// takes a history store and a logger it uses only for the durable activity-log side-writes; tests that
/// do not care about history get a real <see cref="EfAcquisitionHistoryStore"/> over the same in-memory
/// context (harmless, and it lets a test observe the recorded events when it wants to).
/// </summary>
internal static class AcquisitionTestFactory {
    /// <summary>An <see cref="EfAcquisitionStore"/> wired with a real history store over the same context.</summary>
    public static EfAcquisitionStore Store(PrismediaDbContext db) =>
        new(db, new EfAcquisitionHistoryStore(db), NullLogger<EfAcquisitionStore>.Instance);
}
