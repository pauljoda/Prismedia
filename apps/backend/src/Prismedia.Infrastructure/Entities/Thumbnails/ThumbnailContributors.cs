using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Reflection-based factory that discovers every <see cref="IThumbnailContributor"/> implementation
/// in the Infrastructure assembly, bound to a single <see cref="PrismediaDbContext"/>. Mirrors
/// <c>EntityMappers</c>: used by tests that construct the read service directly, while production DI
/// registers the same set. Every contributor takes a single <see cref="PrismediaDbContext"/>
/// constructor parameter so the factory has one path.
/// </summary>
public static class ThumbnailContributors {
    /// <summary>Discovers every concrete thumbnail contributor bound to <paramref name="db"/>.</summary>
    /// <param name="db">Database context the contributors query through.</param>
    public static IReadOnlyList<IThumbnailContributor> For(PrismediaDbContext db) =>
        typeof(ThumbnailContributors).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(IThumbnailContributor).IsAssignableFrom(type))
            .Select(type => (IThumbnailContributor)Activator.CreateInstance(type, db)!)
            .ToArray();
}
