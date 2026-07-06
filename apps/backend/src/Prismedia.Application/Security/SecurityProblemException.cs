namespace Prismedia.Application.Security;

/// <summary>
/// Domain-rule violation raised by the user/auth services, carrying the machine-readable
/// problem code (from <c>ApiProblemCodes</c>) that HTTP endpoints map to a response.
/// </summary>
public sealed class SecurityProblemException : Exception {
    public SecurityProblemException(string code, string message) : base(message) {
        Code = code;
    }

    /// <summary>Machine-readable problem code for the API response.</summary>
    public string Code { get; }
}
