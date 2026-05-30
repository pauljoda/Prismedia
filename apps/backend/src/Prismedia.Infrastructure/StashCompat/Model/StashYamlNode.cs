using YamlDotNet.Serialization;

namespace Prismedia.Infrastructure.StashCompat.Model;

/// <summary>
/// Lightweight dynamic view over a parsed Stash scraper YAML document.
/// Stash community scrapers mix shapes freely — a capability may be a single
/// action map or a list of URL-keyed maps, and selector fields may be a bare
/// string or an object with a <c>selector</c> plus a <c>postProcess</c> pipeline.
/// Navigating a normalized node graph (rather than a rigid POCO) mirrors the
/// reference TypeScript engine and tolerates this variance without binding errors.
/// </summary>
public sealed class StashYamlNode {
    private readonly object? _value;

    private StashYamlNode(object? value) {
        _value = value;
    }

    /// <summary>True when this node holds no value (missing key or null scalar).</summary>
    public bool IsMissing => _value is null;

    /// <summary>True when this node is a mapping (YAML object).</summary>
    public bool IsMap => _value is IReadOnlyDictionary<string, object?>;

    /// <summary>True when this node is a sequence (YAML list).</summary>
    public bool IsList => _value is IReadOnlyList<object?>;

    /// <summary>The scalar string value, or null when this node is not a scalar.</summary>
    public string? Scalar => _value as string;

    /// <summary>
    /// Parses raw YAML text into a normalized node graph.
    /// </summary>
    /// <param name="yaml">Raw YAML scraper definition.</param>
    /// <returns>The root node; <see cref="IsMissing"/> when the document is empty or malformed.</returns>
    public static StashYamlNode Parse(string yaml) {
        var deserializer = new DeserializerBuilder().Build();
        var raw = deserializer.Deserialize<object?>(yaml);
        return new StashYamlNode(Normalize(raw));
    }

    /// <summary>
    /// Returns the child node for a key (case-insensitive), or a missing node.
    /// </summary>
    public StashYamlNode this[string key] {
        get {
            if (_value is IReadOnlyDictionary<string, object?> map &&
                map.TryGetValue(key, out var child)) {
                return new StashYamlNode(child);
            }

            return new StashYamlNode(null);
        }
    }

    /// <summary>Enumerates sequence items; empty when this node is not a list.</summary>
    public IEnumerable<StashYamlNode> Items() {
        if (_value is IReadOnlyList<object?> list) {
            foreach (var item in list) {
                yield return new StashYamlNode(item);
            }
        }
    }

    /// <summary>Enumerates mapping entries; empty when this node is not a map.</summary>
    public IEnumerable<KeyValuePair<string, StashYamlNode>> Entries() {
        if (_value is IReadOnlyDictionary<string, object?> map) {
            foreach (var (key, value) in map) {
                yield return new KeyValuePair<string, StashYamlNode>(key, new StashYamlNode(value));
            }
        }
    }

    /// <summary>True when the mapping contains the given key (case-insensitive).</summary>
    public bool HasKey(string key) =>
        _value is IReadOnlyDictionary<string, object?> map && map.ContainsKey(key);

    /// <summary>Returns the scalar string at a key, trimmed, or null.</summary>
    public string? StringAt(string key) {
        var scalar = this[key].Scalar;
        return string.IsNullOrWhiteSpace(scalar) ? null : scalar.Trim();
    }

    private static object? Normalize(object? value) {
        switch (value) {
            case IDictionary<object, object> dictionary: {
                var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, item) in dictionary) {
                    map[Convert.ToString(key) ?? string.Empty] = Normalize(item);
                }

                return map;
            }
            case IList<object> list:
                return list.Select(Normalize).ToList();
            default:
                return value;
        }
    }
}
