namespace Prismedia.Contracts.System;

/// <summary>
/// Health response for checking whether the .NET backend is ready.
/// </summary>
/// <param name="Status">Readiness status, usually ok.</param>
/// <param name="Runtime">Runtime that served the response.</param>
/// <param name="Version">Plain <c>X.Y.Z</c> build version, so clients can gate newer server contracts.</param>
public sealed record HealthResponse(string Status, string Runtime, string Version);
