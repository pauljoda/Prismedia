using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Prismedia.Application.Integrations;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Operation-specific, expiring protection for inspected executor selections.</summary>
public sealed class ExecutorSelectionProtector(string dataDirectory) : IExecutorSelectionProtector {
    private const string Purpose = "executor-selection-v1";
    private readonly IDataProtectionProvider provider = IntegrationDataProtection.Create(dataDirectory);
    /// <inheritdoc />
    public string Protect(Guid connectionId, AcceptedExecutorSelection selection) {
        var remaining = selection.Inspection.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) throw InvalidSelection();
        var token = Protector(connectionId).Protect(JsonSerializer.Serialize(selection, PluginProcessTransport.JsonOptions),
            remaining > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : remaining);
        if (token.Length > 262144) throw new IntegrationInvocationException("The inspected publication choices exceed the host selection limit.");
        return token;
    }
    /// <inheritdoc />
    public AcceptedExecutorSelection Read(Guid connectionId, string token) {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 262144) throw InvalidSelection();
        try {
            return JsonSerializer.Deserialize<AcceptedExecutorSelection>(Protector(connectionId).Unprotect(token), PluginProcessTransport.JsonOptions)
                ?? throw InvalidSelection();
        } catch (Exception error) when (error is CryptographicException or JsonException) { throw InvalidSelection(); }
    }
    private ITimeLimitedDataProtector Protector(Guid id) => provider.CreateProtector(Purpose, id.ToString("N")).ToTimeLimitedDataProtector();
    private static ArgumentException InvalidSelection() => new("This executor selection is invalid or expired. Inspect the source again.");
}
