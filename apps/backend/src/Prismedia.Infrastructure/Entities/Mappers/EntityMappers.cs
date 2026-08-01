using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers;

/// <summary>
/// Reflection-based factory that discovers every <see cref="IEntityKindMapper"/> and
/// <see cref="IEntityCapabilityMapper"/> implementation in the Infrastructure assembly.
/// Used by the DI registration in <see cref="DependencyInjection"/> and by tests that
/// construct <see cref="EfEntityRepository"/> directly. Mappers take the
/// <see cref="PrismediaDbContext"/> and optionally the current-user context (per-user
/// capability mappers) as constructor parameters.
/// </summary>
public static class EntityMappers {
    /// <summary>Discovers every concrete kind mapper bound to <paramref name="db"/>.</summary>
    public static IReadOnlyList<IEntityKindMapper> Kinds(
        PrismediaDbContext db,
        Prismedia.Application.Security.ICurrentUserContext? currentUser = null) {
        var explicitMappers = Discover<IEntityKindMapper>(db, currentUser);
        var mappedKinds = explicitMappers.Select(mapper => mapper.Kind).ToHashSet();
        var conventionMappers = EntityKindRegistry.All
            .OfType<IEntityRootFactory>()
            .Where(factory => !mappedKinds.Contains(factory.Definition.Kind))
            .Select(factory => new ConventionEntityKindMapper(factory))
            .Cast<IEntityKindMapper>();

        return explicitMappers.Concat(conventionMappers).ToArray();
    }

    /// <summary>Discovers every concrete capability mapper bound to <paramref name="db"/> and <paramref name="currentUser"/>.</summary>
    public static IReadOnlyList<IEntityCapabilityMapper> Capabilities(
        PrismediaDbContext db,
        Prismedia.Application.Security.ICurrentUserContext currentUser) =>
        Discover<IEntityCapabilityMapper>(db, currentUser);

    private static IReadOnlyList<TMapper> Discover<TMapper>(
        PrismediaDbContext db,
        Prismedia.Application.Security.ICurrentUserContext? currentUser) {
        var assembly = typeof(EntityMappers).Assembly;
        return assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(TMapper).IsAssignableFrom(type) &&
                           type != typeof(ConventionEntityKindMapper))
            .Select(type => {
                var takesUserContext = type.GetConstructors().Any(ctor =>
                    ctor.GetParameters().Any(parameter =>
                        parameter.ParameterType == typeof(Prismedia.Application.Security.ICurrentUserContext)));
                return (TMapper)(takesUserContext
                    ? Activator.CreateInstance(type, db, currentUser)!
                    : Activator.CreateInstance(type, db)!);
            })
            .ToArray();
    }

    internal sealed class ConventionEntityKindMapper(IEntityRootFactory factory) : IEntityKindMapper {
        public EntityKind Kind => factory.Definition.Kind;

        public Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) =>
            Task.FromResult(factory.Create(new EntityRootData(
                row.Id,
                row.Title,
                row.ParentEntityId,
                row.SortOrder)));
    }
}
