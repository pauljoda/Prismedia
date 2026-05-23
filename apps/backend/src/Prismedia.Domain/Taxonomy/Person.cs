using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Taxonomy;

/// <summary>
/// Domain model for a person taxonomy entity.
/// </summary>
public sealed class Person : Entity {
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

    public override EntityKind Kind => EntityKind.Person;
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

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityRating(),
        new CapabilityLinks(),
        new CapabilityFlags(),
        new CapabilityFiles(),
        new CapabilityDates(),
        new CapabilityLifetime()
    ];
}
