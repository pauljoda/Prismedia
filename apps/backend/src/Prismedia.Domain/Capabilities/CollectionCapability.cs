namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Base class for capabilities whose state is a single ordered collection of value objects.
/// Owns the backing list and exposes minimal mutation primitives so concrete capabilities can
/// express their own domain operations on top.
/// </summary>
/// <typeparam name="TItem">Value object type held by the capability.</typeparam>
public abstract class CollectionCapability<TItem> : EntityCapability {
    private readonly List<TItem> _items;

    /// <summary>
    /// Creates the capability with an optional initial item list.
    /// </summary>
    /// <param name="items">Initial items, or null for an empty capability.</param>
    protected CollectionCapability(IEnumerable<TItem>? items = null) {
        _items = items is null ? [] : [.. items];
    }

    /// <summary>Immutable view of the items held by this capability in current order.</summary>
    public IReadOnlyList<TItem> Items => _items;

    /// <summary>Appends an item to the end of the collection.</summary>
    /// <param name="item">Item to append.</param>
    protected void AddItem(TItem item) {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    /// <summary>Removes every item matching the predicate.</summary>
    /// <param name="match">Predicate selecting items to remove.</param>
    /// <returns>The number of items removed.</returns>
    protected int RemoveItems(Predicate<TItem> match) {
        ArgumentNullException.ThrowIfNull(match);
        return _items.RemoveAll(match);
    }

    /// <summary>Replaces the entire collection with the supplied items.</summary>
    /// <param name="items">Replacement items.</param>
    protected void ReplaceAll(IEnumerable<TItem> items) {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        _items.AddRange(items);
    }

    /// <summary>Re-sorts the collection in place using the supplied key.</summary>
    /// <typeparam name="TSortKey">Sort key type.</typeparam>
    /// <param name="keySelector">Projects each item to its sort key.</param>
    protected void SortBy<TSortKey>(Func<TItem, TSortKey> keySelector) {
        ArgumentNullException.ThrowIfNull(keySelector);
        var ordered = _items.OrderBy(keySelector).ToArray();
        _items.Clear();
        _items.AddRange(ordered);
    }
}
