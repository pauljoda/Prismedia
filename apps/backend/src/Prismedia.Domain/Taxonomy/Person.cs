using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using PersonProfileDocumentCapability = Prismedia.Contracts.Entities.PersonProfileCapability;

namespace Prismedia.Domain.Taxonomy;

/// <summary>Defines the person taxonomy kind and its lifetime metadata capabilities.</summary>
public sealed class PersonEntityKindDefinition() : EntityKindDefinition<Person>(
    EntityKind.Person,
    "person",
    "Person",
    "People",
    EntityKindCategory.Taxonomy,
    EntityStorageShape.None,
    new EntityKindPresentation(
        EntityKindIcon.Person,
        EntityKindIcon.Person,
        4,
        5,
        EntityAccentHue.Red,
        EntityAccentHue.Violet,
        EntityArtworkFit.Cover),
    new EntityKindNavigation(EntityKind.Person, "people", "/people", "/people/{id}"),
    new EntityKindSearch(3, expandsRelationshipResults: true),
    new EntityKindBehavior(supportsManualManagement: true),
    defaultCapabilities: static () => [new CapabilityDates(), new CapabilityLifetime()]) {
    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(PersonProfileDocumentCapability)];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        Person entity,
        EntityKindProjectionContext context) =>
        [
            new PersonProfileDocumentCapability(
                entity.Disambiguation,
                entity.Gender,
                entity.Country,
                entity.Ethnicity,
                entity.EyeColor,
                entity.HairColor,
                entity.Height,
                entity.Weight,
                entity.Measurements,
                entity.Tattoos,
                entity.Piercings)
        ];
}

/// <summary>
/// Domain model for a person taxonomy entity.
/// </summary>
public sealed class Person : Entity<PersonEntityKindDefinition> {
    public Person(
        Guid id,
        string title,
        string? disambiguation = null,
        string? gender = null,
        string? country = null,
        string? ethnicity = null,
        string? eyeColor = null,
        string? hairColor = null,
        int? height = null,
        int? weight = null,
        string? measurements = null,
        string? tattoos = null,
        string? piercings = null,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        Disambiguation = disambiguation;
        Gender = gender;
        Country = country;
        Ethnicity = ethnicity;
        EyeColor = eyeColor;
        HairColor = hairColor;
        Height = height;
        Weight = weight;
        Measurements = measurements;
        Tattoos = tattoos;
        Piercings = piercings;
    }

    public string? Disambiguation { get; private set; }
    public string? Gender { get; private set; }
    public string? Country { get; private set; }
    public string? Ethnicity { get; private set; }
    public string? EyeColor { get; private set; }
    public string? HairColor { get; private set; }
    public int? Height { get; private set; }
    public int? Weight { get; private set; }
    public string? Measurements { get; private set; }
    public string? Tattoos { get; private set; }
    public string? Piercings { get; private set; }

    /// <summary>Updates the country value for the person.</summary>
    public void SetCountry(string? country) {
        Country = country;
    }
}
