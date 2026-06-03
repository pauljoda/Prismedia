namespace Prismedia.Application.Jobs.Scanning;

/// <summary>
/// A single discovered file reduced to the cheap signals used to detect change between scans:
/// its absolute path plus a content signature (size and last-write time in UTC ticks). No hashing
/// is involved — the signature is gathered from the same directory walk that discovers the file, so
/// an incremental rescan can decide what changed without re-reading file contents.
/// </summary>
/// <param name="Path">Absolute file path.</param>
/// <param name="SizeBytes">File length in bytes.</param>
/// <param name="ModifiedTicks">Last-write time, in UTC ticks.</param>
public sealed record FileSignature(string Path, long SizeBytes, long ModifiedTicks);
