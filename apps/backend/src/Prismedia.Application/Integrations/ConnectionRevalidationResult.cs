namespace Prismedia.Application.Integrations;

/// <summary>Outcome of one bounded automatic connection-revalidation pass.</summary>
public sealed record ConnectionRevalidationResult(int Ready, int Failed, int Deferred, bool MayHaveMore);
