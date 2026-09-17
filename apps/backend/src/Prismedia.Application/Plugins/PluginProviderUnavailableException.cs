namespace Prismedia.Application.Plugins;

/// <summary>A metadata provider failed to answer; this is distinct from a successful lookup with no match.</summary>
/// <param name="message">Safe provider error text after the plugin runner has removed credentials.</param>
public sealed class PluginProviderUnavailableException(string message) : InvalidOperationException(message);
