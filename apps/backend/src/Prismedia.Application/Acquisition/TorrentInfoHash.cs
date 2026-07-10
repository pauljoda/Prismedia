using System.Security.Cryptography;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Extracts the BitTorrent v1 info hash from an uploaded metainfo document. The hash covers the exact
/// bencoded bytes of the top-level <c>info</c> value, so it remains identical to the native id used by
/// torrent clients and can safely anchor a crash-recovery handoff.
/// </summary>
public static class TorrentInfoHash {
    private const int MaxNestingDepth = 64;

    /// <summary>
    /// Computes the lowercase SHA-1 info hash, or returns <see langword="null"/> when the payload is not
    /// a complete bencoded dictionary with exactly one <c>info</c> dictionary, or when it is pure v2 and
    /// therefore has no compatible SHA-1 native id. Hybrid torrents retain their v1 identity.
    /// </summary>
    public static string? TryComputeV1(ReadOnlySpan<byte> torrent) {
        var offset = 0;
        if (torrent.IsEmpty || torrent[offset++] != (byte)'d') {
            return null;
        }

        var infoStart = -1;
        var infoLength = 0;
        while (offset < torrent.Length && torrent[offset] != (byte)'e') {
            if (!TryReadString(torrent, ref offset, out var keyStart, out var keyLength)) {
                return null;
            }

            var valueStart = offset;
            if (!TrySkipValue(torrent, ref offset, depth: 1)) {
                return null;
            }

            // prism-vocab: external — BitTorrent bencode dictionary keys are decoded only here.
            if (!torrent.Slice(keyStart, keyLength).SequenceEqual("info"u8)) {
                continue;
            }

            if (infoStart >= 0 || torrent[valueStart] != (byte)'d') {
                return null;
            }

            infoStart = valueStart;
            infoLength = offset - valueStart;
        }

        if (offset >= torrent.Length || torrent[offset++] != (byte)'e' || offset != torrent.Length || infoStart < 0) {
            return null;
        }

        var info = torrent.Slice(infoStart, infoLength);
        // Pure v2 torrents use the SHA-256 info hash. Persisting a SHA-1 here would look like a reliable
        // client-native id but could never resolve. Hybrid torrents still contain the v1 `pieces` field,
        // so their SHA-1 identity remains the compatible direct-lookup key.
        if (IsPureV2(info)) {
            return null;
        }

        // SHA-1 is the protocol-defined BitTorrent v1 identity, not a password or signature primitive.
        return Convert.ToHexString(SHA1.HashData(info))
            .ToLowerInvariant();
    }

    private static bool IsPureV2(ReadOnlySpan<byte> info) {
        var offset = 0;
        if (info.IsEmpty || info[offset++] != (byte)'d') {
            return false;
        }

        var hasV1Pieces = false;
        var hasV2MetaVersion = false;
        while (offset < info.Length && info[offset] != (byte)'e') {
            if (!TryReadString(info, ref offset, out var keyStart, out var keyLength)) {
                return false;
            }

            var valueStart = offset;
            if (!TrySkipValue(info, ref offset, depth: 1)) {
                return false;
            }

            // prism-vocab: external — BEP 52 declares these info-dictionary keys.
            var key = info.Slice(keyStart, keyLength);
            hasV1Pieces |= key.SequenceEqual("pieces"u8);
            hasV2MetaVersion |= key.SequenceEqual("meta version"u8)
                && info.Slice(valueStart, offset - valueStart).SequenceEqual("i2e"u8);
        }

        return hasV2MetaVersion && !hasV1Pieces;
    }

    private static bool TrySkipValue(ReadOnlySpan<byte> data, ref int offset, int depth) {
        if (offset >= data.Length || depth > MaxNestingDepth) {
            return false;
        }

        var marker = data[offset];
        if (marker is >= (byte)'0' and <= (byte)'9') {
            return TryReadString(data, ref offset, out _, out _);
        }

        if (marker == (byte)'i') {
            return TrySkipInteger(data, ref offset);
        }

        if (marker != (byte)'l' && marker != (byte)'d') {
            return false;
        }

        var dictionary = marker == (byte)'d';
        offset++;
        while (offset < data.Length && data[offset] != (byte)'e') {
            if (dictionary && !TryReadString(data, ref offset, out _, out _)) {
                return false;
            }

            if (!TrySkipValue(data, ref offset, depth + 1)) {
                return false;
            }
        }

        if (offset >= data.Length || data[offset] != (byte)'e') {
            return false;
        }

        offset++;
        return true;
    }

    private static bool TrySkipInteger(ReadOnlySpan<byte> data, ref int offset) {
        offset++;
        if (offset >= data.Length) {
            return false;
        }

        var negative = data[offset] == (byte)'-';
        if (negative && ++offset >= data.Length) {
            return false;
        }

        var firstDigit = offset;
        while (offset < data.Length && data[offset] is >= (byte)'0' and <= (byte)'9') {
            offset++;
        }

        if (offset == firstDigit
            || offset >= data.Length
            || data[offset] != (byte)'e'
            || (data[firstDigit] == (byte)'0' && offset - firstDigit > 1)
            || (negative && data[firstDigit] == (byte)'0')) {
            return false;
        }

        offset++;
        return true;
    }

    private static bool TryReadString(
        ReadOnlySpan<byte> data,
        ref int offset,
        out int valueStart,
        out int valueLength) {
        valueStart = 0;
        valueLength = 0;
        if (offset >= data.Length || data[offset] is < (byte)'0' or > (byte)'9') {
            return false;
        }

        var firstDigit = offset;
        var length = 0;
        while (offset < data.Length && data[offset] is >= (byte)'0' and <= (byte)'9') {
            var digit = data[offset++] - (byte)'0';
            if (length > (int.MaxValue - digit) / 10) {
                return false;
            }
            length = (length * 10) + digit;
        }

        if (offset >= data.Length
            || data[offset++] != (byte)':'
            || (data[firstDigit] == (byte)'0' && offset - firstDigit > 2)
            || length > data.Length - offset) {
            return false;
        }

        valueStart = offset;
        valueLength = length;
        offset += length;
        return true;
    }
}
