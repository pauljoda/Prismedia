using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Navigation;

namespace Prismedia.Application.Navigation;

/// <summary>
/// Application use-case service for the user's server-persisted navigation layout.
/// Stores the layout document as raw JSON in the shared app-settings key/value store
/// under a reserved key that is intentionally not part of the settings catalog, so it
/// never surfaces in the settings UI.
/// </summary>
public sealed class NavLayoutService {
    /// <summary>
    /// Reserved app-settings key holding the navigation layout JSON document. Not a
    /// registered catalog setting.
    /// </summary>
    internal const string LayoutKey = "ui.navigation-layout";

    private static readonly JsonSerializerOptions SerializerOptions = JsonSerializerOptions.Web;

    private readonly ISettingsPersistence _persistence;
    private readonly ILogger<NavLayoutService>? _logger;

    /// <summary>
    /// Creates the service over the settings persistence port.
    /// </summary>
    /// <param name="persistence">Persistence adapter implemented by Infrastructure.</param>
    /// <param name="logger">Optional logger used when a stored layout fails to deserialize.</param>
    public NavLayoutService(ISettingsPersistence persistence, ILogger<NavLayoutService>? logger = null) {
        _persistence = persistence;
        _logger = logger;
    }

    /// <summary>
    /// Returns the stored navigation layout, or <c>null</c> when none has been saved or the
    /// stored value is not valid JSON (the client then falls back to its seeded default).
    /// </summary>
    public async Task<NavLayoutDocument?> GetAsync(CancellationToken cancellationToken) {
        var overrides = await _persistence.LoadSettingOverridesAsync(cancellationToken);
        if (!overrides.TryGetValue(LayoutKey, out var rawJson) || string.IsNullOrWhiteSpace(rawJson)) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<NavLayoutDocument>(rawJson, SerializerOptions);
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, "Stored navigation layout is invalid JSON and will be ignored.");
            return null;
        }
    }

    /// <summary>
    /// Persists the navigation layout document, replacing any previously stored layout.
    /// </summary>
    public async Task<NavLayoutDocument> SaveAsync(NavLayoutDocument layout, CancellationToken cancellationToken) {
        var json = JsonSerializer.Serialize(layout, SerializerOptions);
        await _persistence.SaveSettingOverrideAsync(LayoutKey, json, cancellationToken);
        return layout;
    }
}
