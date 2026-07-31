namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Direct library-root ownership for an entity that is independently rooted in a library.</summary>
public class EntityLibraryRootRow {
    public Guid EntityId { get; set; }

    public Guid? LibraryRootId { get; set; }
}
