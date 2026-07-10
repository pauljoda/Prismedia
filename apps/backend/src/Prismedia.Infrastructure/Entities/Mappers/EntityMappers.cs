using System.Reflection;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Series;
using Prismedia.Contracts.Taxonomy;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
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
    public static IReadOnlyList<IEntityKindMapper> Kinds(PrismediaDbContext db) {
        var explicitMappers = Discover<IEntityKindMapper>(db, currentUser: null);
        var mappedKinds = explicitMappers.Select(mapper => mapper.Kind).ToHashSet();
        var conventionMappers = EntityKindRegistry.All
            .Where(descriptor => descriptor.ClrType is not null && !mappedKinds.Contains(descriptor.Value))
            .Select(descriptor => new ConventionEntityKindMapper(descriptor))
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

    internal sealed class ConventionEntityKindMapper(EntityKindDescriptor descriptor) : IEntityKindMapper {
        public EntityKind Kind => descriptor.Value;

        public Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
            if (descriptor.ClrType is null) {
                throw new InvalidOperationException($"EntityKind.{descriptor.Value} has no domain type.");
            }

            if (descriptor.ClrType == typeof(VideoSeason)) {
                return Task.FromResult<Entity>(new VideoSeason(row.Id, row.Title, row.ParentEntityId, sortOrder: row.SortOrder));
            }

            if (FindSimpleConstructor(descriptor.ClrType) is { } ctor) {
                var args = ctor.GetParameters()
                    .Select(parameter => ArgumentFor(row, parameter))
                    .ToArray();
                return Task.FromResult((Entity)ctor.Invoke(args));
            }

            throw new InvalidOperationException(
                $"EntityKind.{descriptor.Value} cannot be convention-hydrated; add an explicit IEntityKindMapper.");
        }

        public Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public IEntityCard ProjectDetail(
            Entity entity,
            EntityCard card,
            IReadOnlyList<EntityCreditMetadata> creditMetadata) =>
            descriptor.Value switch {
                EntityKind.AudioLibrary => new AudioLibraryDetail {
                    Id = card.Id,
                    Kind = card.Kind,
                    Title = card.Title,
                    ParentEntityId = card.ParentEntityId,
                    SortOrder = card.SortOrder,
                    HasSourceMedia = card.HasSourceMedia,
                    Capabilities = card.Capabilities,
                    ChildrenByKind = card.ChildrenByKind,
                    Relationships = card.Relationships,
                },
                EntityKind.MusicArtist => new MusicArtistDetail {
                    Id = card.Id,
                    Kind = card.Kind,
                    Title = card.Title,
                    ParentEntityId = card.ParentEntityId,
                    SortOrder = card.SortOrder,
                    HasSourceMedia = card.HasSourceMedia,
                    Capabilities = card.Capabilities,
                    ChildrenByKind = card.ChildrenByKind,
                    Relationships = card.Relationships,
                    CreditMetadata = creditMetadata,
                },
                EntityKind.BookAuthor => new BookAuthorDetail {
                    Id = card.Id,
                    Kind = card.Kind,
                    Title = card.Title,
                    ParentEntityId = card.ParentEntityId,
                    SortOrder = card.SortOrder,
                    HasSourceMedia = card.HasSourceMedia,
                    Capabilities = card.Capabilities,
                    ChildrenByKind = card.ChildrenByKind,
                    Relationships = card.Relationships,
                    CreditMetadata = creditMetadata,
                },
                EntityKind.Image => new ImageDetail {
                    Id = card.Id,
                    Kind = card.Kind,
                    Title = card.Title,
                    ParentEntityId = card.ParentEntityId,
                    SortOrder = card.SortOrder,
                    HasSourceMedia = card.HasSourceMedia,
                    Capabilities = card.Capabilities,
                    ChildrenByKind = card.ChildrenByKind,
                    Relationships = card.Relationships,
                },
                EntityKind.Studio => new StudioDetail {
                    Id = card.Id,
                    Kind = card.Kind,
                    Title = card.Title,
                    ParentEntityId = card.ParentEntityId,
                    SortOrder = card.SortOrder,
                    HasSourceMedia = card.HasSourceMedia,
                    Capabilities = card.Capabilities,
                    ChildrenByKind = card.ChildrenByKind,
                    Relationships = card.Relationships,
                },
                EntityKind.VideoSeason => new VideoSeasonDetail {
                    Id = card.Id,
                    Kind = card.Kind,
                    Title = card.Title,
                    ParentEntityId = card.ParentEntityId,
                    SortOrder = card.SortOrder,
                    HasSourceMedia = card.HasSourceMedia,
                    Capabilities = card.Capabilities,
                    ChildrenByKind = card.ChildrenByKind,
                    Relationships = card.Relationships,
                    CreditMetadata = creditMetadata,
                },
                _ => card
            };

        private static ConstructorInfo? FindSimpleConstructor(Type type) =>
            type.GetConstructors()
                .Where(ctor => {
                    var parameters = ctor.GetParameters();
                    return parameters.Length >= 2 &&
                           parameters[0].ParameterType == typeof(Guid) &&
                           parameters[1].ParameterType == typeof(string) &&
                           parameters.Skip(2).All(parameter => parameter.HasDefaultValue);
                })
                .OrderBy(ctor => ctor.GetParameters().Length)
                .FirstOrDefault();

        private static object? ArgumentFor(EntityRow row, ParameterInfo parameter) {
            if (parameter.ParameterType == typeof(Guid)) {
                return row.Id;
            }

            if (parameter.ParameterType == typeof(string)) {
                return row.Title;
            }

            if (parameter.Name == "parentEntityId" && parameter.ParameterType == typeof(Guid?)) {
                return row.ParentEntityId;
            }

            if (parameter.Name == "sortOrder" && parameter.ParameterType == typeof(int?)) {
                return row.SortOrder;
            }

            return parameter.DefaultValue;
        }
    }
}
