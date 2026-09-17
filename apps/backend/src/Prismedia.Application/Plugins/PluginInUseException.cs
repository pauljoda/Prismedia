namespace Prismedia.Application.Plugins;

/// <summary>The installed adapter still owns work or connected-library authority that must survive a package change.</summary>
public sealed class PluginInUseException(string message) : InvalidOperationException(message);
