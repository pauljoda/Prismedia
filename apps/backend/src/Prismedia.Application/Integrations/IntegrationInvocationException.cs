using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>A trusted plugin failed its operation or returned an invalid response.</summary>
public sealed class IntegrationInvocationException(string message, IntegrationErrorCode? code = null) : Exception(message) {
    #region Variables

    /// <summary>Machine-readable provider failure when the plugin established one recognized fact.</summary>
    public IntegrationErrorCode? Code { get; } = code;

    #endregion
}
