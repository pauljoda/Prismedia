namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class PersonDetailRow {
    public Guid EntityId { get; set; }
    public string? Disambiguation { get; set; }
    public string? Gender { get; set; }
    public string? Country { get; set; }
    public string? Ethnicity { get; set; }
    public string? EyeColor { get; set; }
    public string? HairColor { get; set; }
    public int? Height { get; set; }
    public int? Weight { get; set; }
    public string? Measurements { get; set; }
    public string? Tattoos { get; set; }
    public string? Piercings { get; set; }
}

public sealed class TagDetailRow {
    public Guid EntityId { get; set; }
    public bool IgnoreAutoTag { get; set; }
}
