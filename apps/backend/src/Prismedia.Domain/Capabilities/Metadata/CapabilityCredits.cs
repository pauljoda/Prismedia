using Prismedia.Domain.Entities;
using Prismedia.Domain.Taxonomy;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable list of people credited in the scope of the owning entity.
/// </summary>
public sealed class CapabilityCredits(IEnumerable<CapabilityCredits.Item>? items = null)
    : CollectionCapability<CapabilityCredits.Item>(items) {
    /// <summary>
    /// Person credit scoped to one entity, including the role and optional display label for that scope.
    /// </summary>
    public sealed class Item {
        /// <summary>
        /// Creates a scoped person credit.
        /// </summary>
        /// <param name="person">Referenced person entity.</param>
        /// <param name="role">Role the person had for the entity.</param>
        /// <param name="label">Optional scoped label, such as a character name.</param>
        public Item(Person person, CreditRole role, string? label = null) {
            Person = person ?? throw new ArgumentNullException(nameof(person));
            Role = role;
            Label = label;
        }

        /// <summary>Referenced person entity.</summary>
        public Person Person { get; }

        /// <summary>Role the person had for the entity.</summary>
        public CreditRole Role { get; }

        /// <summary>Optional scoped label, such as a character name.</summary>
        public string? Label { get; }
    }

    /// <summary>Credits attached to the entity in insertion order.</summary>
    public IReadOnlyList<Item> Credits => Items;

    /// <summary>
    /// Adds a person credit in the scope of the owning entity.
    /// </summary>
    /// <param name="person">Referenced person entity.</param>
    /// <param name="role">Role the person had for this entity.</param>
    /// <param name="label">Optional scoped label, such as a character name.</param>
    /// <returns>The created credit entry.</returns>
    /// <exception cref="ArgumentException">Thrown when the same person, role, and label are already credited.</exception>
    public Item Add(Person person, CreditRole role, string? label = null) {
        ArgumentNullException.ThrowIfNull(person);
        if (Items.Any(credit =>
                credit.Person.Id == person.Id &&
                credit.Role == role &&
                string.Equals(credit.Label, label, StringComparison.Ordinal))) {
            throw new ArgumentException($"Person '{person.Id}' already has a matching {role} credit.", nameof(person));
        }

        var credit = new Item(person, role, label);
        AddItem(credit);
        return credit;
    }

    /// <summary>
    /// Gets credits for a specific role.
    /// </summary>
    /// <param name="role">Credit role to retrieve.</param>
    /// <returns>Matching credits in insertion order.</returns>
    public IReadOnlyList<Item> ForRole(CreditRole role) =>
        Items.Where(credit => credit.Role == role).ToArray();
}
