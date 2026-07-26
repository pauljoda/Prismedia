namespace Prismedia.Domain.Entities;

/// <summary>CPU resource profile applied before a graph node is claimed.</summary>
public enum JobResourceClass {
    [Code("light")]
    Light,

    [Code("standard-cpu")]
    StandardCpu,

    [Code("heavy-cpu")]
    HeavyCpu
}
