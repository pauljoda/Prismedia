namespace Prismedia.Contracts.System;

/// <summary>
/// Health response for checking whether the .NET backend is ready.
/// </summary>
/// <param name="Status">Readiness status, usually ok.</param>
/// <param name="Runtime">Runtime that served the response.</param>
public sealed record HealthResponse(string Status, string Runtime);
