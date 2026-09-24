namespace Prismedia.Application.Health;

/// <summary>
/// Identity of the running Prismedia build, resolved once at startup from <c>PRISMEDIA_VERSION</c>
/// or the root <c>package.json</c>. Clients read it from the health route to decide which server
/// contracts they may rely on.
/// </summary>
/// <param name="Version">Plain <c>X.Y.Z</c> build version.</param>
public sealed record PrismediaBuildInfo(string Version);
