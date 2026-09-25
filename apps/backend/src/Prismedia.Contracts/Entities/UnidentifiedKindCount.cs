namespace Prismedia.Contracts.Entities;

/// <summary>How many library items of one kind still wait for identification: they have media but are not organized.</summary>
/// <param name="Kind">Entity kind code.</param>
/// <param name="Count">Unorganized items of that kind with source media, excluding wanted placeholders.</param>
public sealed record UnidentifiedKindCount(string Kind, int Count);
