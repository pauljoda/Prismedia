using System.Xml;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Prismedia.Application.Files;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>Loads bounded, inert image assets declared by plugin manifests.</summary>
internal static class PluginIconAssetReader {
    internal const int MaximumBytes = 256 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly HashSet<string> SvgElements = new(StringComparer.OrdinalIgnoreCase) {
        "svg", "title", "desc", "g", "defs", "linearGradient", "radialGradient", "stop",
        "clipPath", "mask", "use", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon"
    };

    internal static bool TryRead(PluginDescriptor descriptor, out PluginIconAsset? asset) =>
        TryRead(descriptor.WorkingDirectory, descriptor.Manifest.Icon, out asset);

    internal static bool TryRead(string directory, string? relativePath, out PluginIconAsset? asset) {
        asset = null;
        if (!PluginManifestContract.IsValidIconPath(relativePath) || relativePath is null) return false;

        var path = Path.GetFullPath(Path.Combine(directory, relativePath));
        if (!FileSystemPathComparison.IsSameOrDescendant(directory, path)) return false;

        try {
            if (ContainsSymbolicLink(directory, relativePath)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumBytes) return false;
            var content = new byte[(int)stream.Length];
            stream.ReadExactly(content);
            if (!ValidateContent(content, relativePath, out var contentType)) return false;
            asset = Create(content, contentType!);
            return true;
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }

    internal static bool TryRead(byte[] content, string path, out PluginIconAsset? asset) {
        asset = null;
        if (content.Length is <= 0 or > MaximumBytes || !ValidateContent(content, path, out var contentType)) return false;
        asset = Create(content, contentType!);
        return true;
    }

    private static PluginIconAsset Create(byte[] content, string contentType) =>
        new(content, contentType, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

    private static bool ContainsSymbolicLink(string directory, string relativePath) {
        var current = Path.GetFullPath(directory);
        foreach (var segment in relativePath.Split('/')) {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) return true;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    private static bool ValidateContent(byte[] content, string path, out string? contentType) {
        contentType = null;
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
            if (!content.AsSpan().StartsWith(PngSignature)) return false;
            contentType = "image/png";
            return true;
        }

        if (!IsSafeSvg(content)) return false;
        contentType = "image/svg+xml";
        return true;
    }

    private static bool IsSafeSvg(byte[] content) {
        try {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes
            });
            var sawRoot = false;
            while (reader.Read()) {
                if (reader.NodeType == XmlNodeType.ProcessingInstruction) return false;
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.Depth == 0) {
                    if (!reader.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase) ||
                        reader.NamespaceURI != "http://www.w3.org/2000/svg") return false;
                    sawRoot = true;
                }
                if (reader.NamespaceURI.Length > 0 && reader.NamespaceURI != "http://www.w3.org/2000/svg") return false;
                if (!SvgElements.Contains(reader.LocalName)) return false;
                if (!HasSafeAttributes(reader)) return false;
            }
            return sawRoot;
        } catch (XmlException) {
            return false;
        }
    }

    private static bool HasSafeAttributes(XmlReader reader) {
        if (!reader.HasAttributes) return true;
        while (reader.MoveToNextAttribute()) {
            if (reader.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)) return false;
            if (reader.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (Match match in Regex.Matches(reader.Value, @"url\(([^)]*)\)", RegexOptions.IgnoreCase)) {
                var target = match.Groups[1].Value.Trim().Trim('\'', '"');
                if (!target.StartsWith('#')) return false;
            }
            if (reader.LocalName is not ("href" or "src")) continue;
            var value = reader.Value.Trim();
            if (value.Length > 0 && !value.StartsWith('#')) return false;
        }
        reader.MoveToElement();
        return true;
    }
}
